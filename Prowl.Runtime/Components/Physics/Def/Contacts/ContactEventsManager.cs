﻿using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuUtilities;
using BepuUtilities.Collections;
using BepuUtilities.Memory;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.Components.NewPhysics.Contacts;

/// <summary>
/// Watches a set of bodies and statics for contact changes and reports events.
/// </summary>
internal class ContactEventsManager : IDisposable
{
    //To know what events to emit, we have to track the previous state of a collision. We don't need to keep around old positions/offets/normals/depths, so it's quite a bit lighter.
    [StructLayout(LayoutKind.Sequential)]
    struct PreviousCollision
    {
        public CollidableReference Collidable;
        public bool Fresh;
        public bool WasTouching;
        public int ContactCount;
        //FeatureIds are identifiers encoding what features on the involved shapes contributed to the contact. We store up to 4 feature ids, one for each potential contact.
        //A "feature" is things like a face, vertex, or edge. There is no single interpretation for what a feature is- the mapping is defined on a per collision pair level.
        //In this demo, we only care to check whether a given contact in the current frame maps onto a contact from a previous frame.
        //We can use this to only emit 'contact added' events when a new contact with an unrecognized id is reported.
        public int FeatureId0;
        public int FeatureId1;
        public int FeatureId2;
        public int FeatureId3;
    }

    IThreadDispatcher? threadDispatcher;
    BufferPool? pool;

    //We'll use a handle->index mapping in a CollidableProperty to point at our contiguously stored listeners (in the later listeners array).
    //Note that there's also IndexSets for the statics and bodies; those will be checked first before accessing the listenerIndices.
    //The CollidableProperty is quite barebones- it doesn't try to stop all invalid accesses, and the backing memory isn't guaranteed to be zero initialized.
    //IndexSets are tightly bitpacked and are cheap to access, so they're an easy way to check if a collidable can trigger an event before doing any further processing.
    CollidableProperty<int> listenerIndices;
    IndexSet staticListenerFlags;
    IndexSet bodyListenerFlags;
    int listenerCount;

    //For the purpose of this demo, we'll use some regular ol' interfaces rather than using the struct-implementing-interface for specialization.
    //This array will be GC tracked as a result, but that should be mostly fine. If you've got hundreds of thousands of event handlers, you may want to consider alternatives.
    struct Listener
    {
        public CollidableReference Source;
        public IContactEventHandler Handler;
        public QuickList<PreviousCollision> PreviousCollisions;
    }
    Listener[] listeners;

    //The callbacks are invoked from a multithreaded context, and we don't know how many pairs will exist.
    //Rather than attempting to synchronize all accesses, every worker thread spits out the results into a worker-local list to be processed later by the main thread flush.
    struct PendingWorkerAdd
    {
        public int ListenerIndex;
        public PreviousCollision Collision;
    }
    QuickList<PendingWorkerAdd>[] pendingWorkerAdds;

    /// <summary>
    /// Creates a new contact events stream.
    /// </summary>
    /// <param name="threadDispatcher">Thread dispatcher to pull per-thread buffer pools from, if any.</param>
    /// <param name="pool">Buffer pool used to manage resources internally. If null, the simulation's pool will be used.</param>
    /// <param name="initialListenerCapacity">Number of listeners to allocate space for initially.</param>
#pragma warning disable CS8618 // Unassigned null fields, will be initialized through Initialize() below
    public ContactEventsManager(IThreadDispatcher? threadDispatcher = null, BufferPool? pool = null, int initialListenerCapacity = 64)
#pragma warning restore CS8618
    {
        this.threadDispatcher = threadDispatcher;
        this.pool = pool;
        listeners = new Listener[initialListenerCapacity];
    }

    BufferPool? GetPoolForWorker(int workerIndex)
    {
        return threadDispatcher == null ? pool : threadDispatcher.WorkerPools[workerIndex];
    }

    /// <summary>
    /// Initializes the contact events system with a simulation.
    /// </summary>
    /// <param name="simulation">Simulation to use with the contact events demo.</param>
    /// <remarks>The constructor and initialization are split because of how this class is expected to be used.
    /// It will be passed into a simulation's constructor as a part of its contact callbacks, so there is no simulation available at the time of construction.</remarks>
    public void Initialize()
    {
        pool ??= Physics.Sim.BufferPool;
        Physics.Sim.Timestepper.BeforeCollisionDetection += SetFreshnessForCurrentActivityStatus;
        listenerIndices = new CollidableProperty<int>(Physics.Sim, pool);
        pendingWorkerAdds = new QuickList<PendingWorkerAdd>[threadDispatcher == null ? 1 : threadDispatcher.ThreadCount];
    }

    /// <summary>
    /// Begins listening for events related to the given collidable.
    /// </summary>
    /// <param name="collidable">Collidable to monitor for events.</param>
    /// <param name="handler">Handlers to use for the collidable.</param>
    public void Register(CollidableReference collidable, IContactEventHandler handler)
    {
        if (collidable.Mobility == CollidableMobility.Static)
            staticListenerFlags.Add(collidable.RawHandleValue, pool);
        else
            bodyListenerFlags.Add(collidable.RawHandleValue, pool);
        if (listenerCount >= listeners.Length)
        {
            Array.Resize(ref listeners, listeners.Length * 2);
        }
        //Note that allocations for the previous collision list are deferred until they actually exist.
        listeners[listenerCount] = new Listener { Handler = handler, Source = collidable };
        listenerIndices[collidable] = listenerCount;
        ++listenerCount;
    }

    /// <summary>
    /// Begins listening for events related to the given body.
    /// </summary>
    /// <param name="body">Body to monitor for events.</param>
    /// <param name="handler">Handlers to use for the body.</param>
    public void Register(BodyHandle body, IContactEventHandler handler)
    {
        Register(Physics.Sim.Bodies[body].CollidableReference, handler);
    }

    /// <summary>
    /// Begins listening for events related to the given static.
    /// </summary>
    /// <param name="staticHandle">Static to monitor for events.</param>
    /// <param name="handler">Handlers to use for the static.</param>
    public void Register(StaticHandle staticHandle, IContactEventHandler handler)
    {
        Register(new CollidableReference(staticHandle), handler);
    }

    /// <summary>
    /// Stops listening for events related to the given collidable.
    /// </summary>
    /// <param name="collidable">Collidable to stop listening for.</param>
    public void Unregister(CollidableReference collidable)
    {
        if (collidable.Mobility == CollidableMobility.Static)
        {
            staticListenerFlags.Remove(collidable.RawHandleValue);
        }
        else
        {
            bodyListenerFlags.Remove(collidable.RawHandleValue);
        }
        var index = listenerIndices[collidable];
        --listenerCount;
        ref var removedSlot = ref listeners[index];
        if (removedSlot.PreviousCollisions.Span.Allocated)
            removedSlot.PreviousCollisions.Dispose(pool);
        ref var lastSlot = ref listeners[listenerCount];
        if (index < listenerCount)
        {
            listenerIndices[lastSlot.Source] = index;
            removedSlot = lastSlot;
        }
        lastSlot = default;
    }

    /// <summary>
    /// Stops listening for events related to the given body.
    /// </summary>
    /// <param name="body">Body to stop listening for.</param>
    public void Unregister(BodyHandle body)
    {
        Unregister(Physics.Sim.Bodies[body].CollidableReference);
    }

    /// <summary>
    /// Stops listening for events related to the given static.
    /// </summary>
    /// <param name="staticHandle">Static to stop listening for.</param>
    public void Unregister(StaticHandle staticHandle)
    {
        Unregister(new CollidableReference(staticHandle));
    }

    /// <summary>
    /// Checks if a collidable is registered as a listener.
    /// </summary>
    /// <param name="collidable">Collidable to check.</param>
    /// <returns>True if the collidable has been registered as a listener, false otherwise.</returns>
    public bool IsListener(CollidableReference collidable)
    {
        if (collidable.Mobility == CollidableMobility.Static)
        {
            return staticListenerFlags.Contains(collidable.RawHandleValue);
        }
        else
        {
            return bodyListenerFlags.Contains(collidable.RawHandleValue);
        }
    }

    /// <summary>
    /// Checks if a collidable is registered as a listener.
    /// </summary>
    public bool IsListener(BodyHandle body)
    {
        return IsListener(Physics.Sim.Bodies[body].CollidableReference);
    }
    /// <summary>
    /// Checks if a collidable is registered as a listener.
    /// </summary>
    public bool IsListener(StaticHandle staticHandle)
    {
        return IsListener(Physics.Sim.Statics[staticHandle].CollidableReference);
    }
    /// <summary>
    /// Callback attached to the simulation's ITimestepper which executes just prior to collision detection to take a snapshot of activity states to determine which pairs we should expect updates in.
    /// </summary>
    void SetFreshnessForCurrentActivityStatus(float dt, IThreadDispatcher threadDispatcher)
    {
        //Every single pair tracked by the contact events has a 'freshness' flag. If the final flush sees a pair that is stale, it'll remove it
        //and any necessary events to represent the end of that pair are reported.
        //HandleManifoldForCollidable sets 'Fresh' to true for any processed pair, but pairs between sleeping or static bodies will not show up in HandleManifoldForCollidable since they're not active.
        //We don't want Flush to report that sleeping pairs have stopped colliding, so we pre-initialize any such sleeping/static pair as 'fresh'.

        //This could be multithreaded reasonably easily if there are a ton of listeners or collisions, but that would be a pretty high bar.
        //For simplicity, the demo will keep it single threaded.
        var bodyHandleToLocation = Physics.Sim.Bodies.HandleToLocation;
        for (int listenerIndex = 0; listenerIndex < listenerCount; ++listenerIndex)
        {
            ref var listener = ref listeners[listenerIndex];
            var source = listener.Source;
            //If it's a body, and it's in the active set (index 0), then every pair associated with the listener should expect updates.
            var sourceExpectsUpdates = source.Mobility != CollidableMobility.Static && bodyHandleToLocation[source.BodyHandle.Value].SetIndex == 0;
            if (sourceExpectsUpdates)
            {
                var previousCollisions = listeners[listenerIndex].PreviousCollisions;
                for (int j = 0; j < previousCollisions.Count; ++j)
                {
                    //Pair updates will set the 'freshness' to true when they happen, so that they won't be considered 'stale' in the flush and removed.
                    previousCollisions[j].Fresh = false;
                }
            }
            else
            {
                //The listener is either static or sleeping. We should only expect updates if the other collidable is awake.
                var previousCollisions = listeners[listenerIndex].PreviousCollisions;
                for (int j = 0; j < previousCollisions.Count; ++j)
                {
                    ref var previousCollision = ref previousCollisions[j];
                    previousCollision.Fresh = previousCollision.Collidable.Mobility == CollidableMobility.Static || bodyHandleToLocation[previousCollision.Collidable.BodyHandle.Value].SetIndex > 0;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void UpdatePreviousCollision<TManifold>(ref PreviousCollision collision, ref TManifold manifold, bool isTouching) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        //If the above assert gets hit because of a change to nonconvex manifold capacities, the packed feature id representation this uses will need to be updated.
        //I very much doubt the nonconvex manifold will ever use more than 8 contacts, so addressing this wouldn't require much of a change.
        for (int j = 0; j < manifold.Count; ++j)
        {
            Unsafe.Add(ref collision.FeatureId0, j) = manifold.GetFeatureId(j);
        }
        collision.ContactCount = manifold.Count;
        collision.Fresh = true;
        collision.WasTouching = isTouching;
    }

    void HandleManifoldForCollidable<TManifold>(int workerIndex, CollidableReference source, CollidableReference other, ref TManifold manifold) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        //The "source" refers to the object that an event handler was (potentially) attached to, so we look for listeners registered for it.
        //(This function is called for both orders of the pair, so we'll catch listeners for either.)
        if (!IsListener(source))
            return;

        var listenerIndex = listenerIndices[source];
        //This collidable is registered. Is the opposing collidable present?
        ref var listener = ref listeners[listenerIndex];

        int previousCollisionIndex = -1;
        bool isTouching = false;
        for (int i = 0; i < listener.PreviousCollisions.Count; ++i)
        {
            ref var collision = ref listener.PreviousCollisions[i];
            //Since the 'Packed' field contains both the handle type (dynamic, kinematic, or static) and the handle index packed into a single bitfield, an equal value guarantees we are dealing with the same collidable.
            if (collision.Collidable.Packed == other.Packed)
            {
                previousCollisionIndex = i;
                //This manifold is associated with an existing collision.
                //For every contact in the old collsion still present (by feature id), set a flag in this bitmask so we can know when a contact is removed.
                int previousContactsStillExist = 0;
                for (int contactIndex = 0; contactIndex < manifold.Count; ++contactIndex)
                {
                    //We can check if each contact was already present in the previous frame by looking at contact feature ids. See the 'PreviousCollision' type for a little more info on FeatureIds.
                    var featureId = manifold.GetFeatureId(contactIndex);
                    var featureIdWasInPreviousCollision = false;
                    for (int previousContactIndex = 0; previousContactIndex < collision.ContactCount; ++previousContactIndex)
                    {
                        if (featureId == Unsafe.Add(ref collision.FeatureId0, previousContactIndex))
                        {
                            featureIdWasInPreviousCollision = true;
                            previousContactsStillExist |= 1 << previousContactIndex;
                            break;
                        }
                    }
                    if (!featureIdWasInPreviousCollision)
                    {
                        listener.Handler.OnContactAdded(source, other, ref manifold, contactIndex, workerIndex);
                    }
                    if (manifold.GetDepth(contactIndex) >= 0)
                        isTouching = true;
                }
                if (previousContactsStillExist != (1 << collision.ContactCount) - 1)
                {
                    //At least one contact that used to exist no longer does.
                    for (int previousContactIndex = 0; previousContactIndex < collision.ContactCount; ++previousContactIndex)
                    {
                        if ((previousContactsStillExist & 1 << previousContactIndex) == 0)
                        {
                            listener.Handler.OnContactRemoved(source, other, ref manifold, Unsafe.Add(ref collision.FeatureId0, previousContactIndex), workerIndex);
                        }
                    }
                }
                if (!collision.WasTouching && isTouching)
                {
                    listener.Handler.OnStartedTouching(source, other, ref manifold, workerIndex);
                }
                else if (collision.WasTouching && !isTouching)
                {
                    listener.Handler.OnStoppedTouching(source, other, ref manifold, workerIndex);
                }
                if (isTouching)
                {
                    listener.Handler.OnTouching(source, other, ref manifold, workerIndex);
                }
                UpdatePreviousCollision(ref collision, ref manifold, isTouching);
                break;
            }
        }
        if (previousCollisionIndex < 0)
        {
            //There was no collision previously.
            ref var addsforWorker = ref pendingWorkerAdds[workerIndex];
            //EnsureCapacity will create the list if it doesn't already exist.
            addsforWorker.EnsureCapacity(Math.Max(addsforWorker.Count + 1, 64), GetPoolForWorker(workerIndex));
            ref var pendingAdd = ref addsforWorker.AllocateUnsafely();
            pendingAdd.ListenerIndex = listenerIndex;
            pendingAdd.Collision.Collidable = other;
            listener.Handler.OnPairCreated(source, other, ref manifold, workerIndex);
            //Dispatch events for all contacts in this new manifold.
            for (int i = 0; i < manifold.Count; ++i)
            {
                listener.Handler.OnContactAdded(source, other, ref manifold, i, workerIndex);
                if (manifold.GetDepth(i) >= 0)
                    isTouching = true;
            }
            if (isTouching)
            {
                listener.Handler.OnStartedTouching(source, other, ref manifold, workerIndex);
                listener.Handler.OnTouching(source, other, ref manifold, workerIndex);
            }
            UpdatePreviousCollision(ref pendingAdd.Collision, ref manifold, isTouching);
        }
        listener.Handler.OnPairUpdated(source, other, ref manifold, workerIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        HandleManifoldForCollidable(workerIndex, pair.A, pair.B, ref manifold);
        HandleManifoldForCollidable(workerIndex, pair.B, pair.A, ref manifold);
    }

    //For final events fired by the flush that still expect a manifold, we'll provide a special empty type.
    struct EmptyManifold : IContactManifold<EmptyManifold>
    {
        public int Count => 0;
        public bool Convex => true;
        //This type never has any contacts, so there's no need for any property grabbers.
        public Contact this[int contactIndex] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public static ref ConvexContact GetConvexContactReference(ref EmptyManifold manifold, int contactIndex) => throw new NotImplementedException();
        public static ref float GetDepthReference(ref EmptyManifold manifold, int contactIndex) => throw new NotImplementedException();
        public static ref int GetFeatureIdReference(ref EmptyManifold manifold, int contactIndex) => throw new NotImplementedException();
        public static ref Contact GetNonconvexContactReference(ref EmptyManifold manifold, int contactIndex) => throw new NotImplementedException();
        public static ref System.Numerics.Vector3 GetNormalReference(ref EmptyManifold manifold, int contactIndex) => throw new NotImplementedException();
        public static ref System.Numerics.Vector3 GetOffsetReference(ref EmptyManifold manifold, int contactIndex) => throw new NotImplementedException();
        public void GetContact(int contactIndex, out System.Numerics.Vector3 offset, out System.Numerics.Vector3 normal, out float depth, out int featureId) => throw new NotImplementedException();
        public void GetContact(int contactIndex, out Contact contactData) => throw new NotImplementedException();
        public float GetDepth(int contactIndex) => throw new NotImplementedException();
        public int GetFeatureId(int contactIndex) => throw new NotImplementedException();
        public System.Numerics.Vector3 GetNormal(int contactIndex) => throw new NotImplementedException();
        public System.Numerics.Vector3 GetOffset(int contactIndex) => throw new NotImplementedException();
    }

    public void Flush()
    {
        //For simplicity, this is completely sequential. Note that it's technically possible to extract more parallelism, but the complexity cost is high and you would need
        //very large numbers of events being processed to make it worth it.

        //Remove any stale collisions. Stale collisions are those which should have received a new manifold update but did not because the manifold is no longer active.
        for (int i = 0; i < listenerCount; ++i)
        {
            ref var listener = ref listeners[i];
            //Note reverse order. We remove during iteration.
            for (int j = listener.PreviousCollisions.Count - 1; j >= 0; --j)
            {
                ref var collision = ref listener.PreviousCollisions[j];
                if (!collision.Fresh)
                {
                    //Sort the references to be consistent with the direct narrow phase results.
                    CollidablePair pair;
                    NarrowPhase.SortCollidableReferencesForPair(listener.Source, collision.Collidable, out _, out _, out pair.A, out pair.B);
                    if (collision.ContactCount > 0)
                    {
                        var emptyManifold = new EmptyManifold();
                        for (int previousContactCount = 0; previousContactCount < collision.ContactCount; ++previousContactCount)
                        {
                            listener.Handler.OnContactRemoved(listener.Source, collision.Collidable, ref emptyManifold, Unsafe.Add(ref collision.FeatureId0, previousContactCount), 0);
                        }
                        if (collision.WasTouching)
                            listener.Handler.OnStoppedTouching(listener.Source, collision.Collidable, ref emptyManifold, 0);
                    }
                    listener.Handler.OnPairEnded(collision.Collidable, collision.Collidable);
                    //This collision was not updated since the last flush despite being active. It should be removed.
                    listener.PreviousCollisions.FastRemoveAt(j);
                    if (listener.PreviousCollisions.Count == 0)
                    {
                        listener.PreviousCollisions.Dispose(pool);
                        listener.PreviousCollisions = default;
                    }
                }
                else
                {
                    collision.Fresh = false;
                }
            }
        }

        for (int i = 0; i < pendingWorkerAdds.Length; ++i)
        {
            ref var pendingAdds = ref pendingWorkerAdds[i];
            for (int j = 0; j < pendingAdds.Count; ++j)
            {
                ref var add = ref pendingAdds[j];
                ref var collisions = ref listeners[add.ListenerIndex].PreviousCollisions;
                //Ensure capacity will initialize the slot if necessary.
                collisions.EnsureCapacity(Math.Max(8, collisions.Count + 1), pool);
                collisions.AllocateUnsafely() = pendingAdds[j].Collision;
            }
            if (pendingAdds.Span.Allocated)
                pendingAdds.Dispose(GetPoolForWorker(i));
            //We rely on zeroing out the count for lazy initialization.
            pendingAdds = default;
        }
    }

    public void Dispose()
    {
        if (bodyListenerFlags.Flags.Allocated)
            bodyListenerFlags.Dispose(pool);
        if (staticListenerFlags.Flags.Allocated)
            staticListenerFlags.Dispose(pool);
        listenerIndices.Dispose();
        Physics.Sim.Timestepper.BeforeCollisionDetection -= SetFreshnessForCurrentActivityStatus;
    }
}