﻿using BepuPhysics.Constraints;

namespace Prowl.Runtime.Components.NewPhysics;


[AddComponentMenu($"{Prowl.Icons.FontAwesome6.HillRockslide}  Physics/{Prowl.Icons.FontAwesome6.Joint}  Constraints/{Prowl.Icons.FontAwesome6.ArrowsToDot}  Center Distance Constraint")]
public sealed class CenterDistanceConstraintComponent : TwoBodyConstraintComponent<CenterDistanceConstraint>
{
    [SerializeField, HideInInspector] private float _targetDistance = 0;
    [SerializeField, HideInInspector] private float _springFrequency = 35;
    [SerializeField, HideInInspector] private float _springDampingRatio = 5;

    [ShowInInspector]
    public float TargetDistance
    {
        get
        {
            return _targetDistance;
        }
        set
        {
            _targetDistance = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float SpringFrequency {
        get {
            return _springFrequency;
        }
        set {
            _springFrequency = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    [ShowInInspector]
    public float SpringDampingRatio {
        get {
            return _springDampingRatio;
        }
        set {
            _springDampingRatio = value;
            ConstraintData?.TryUpdateDescription();
        }
    }

    internal override CenterDistanceConstraint CreateConstraint()
    {
        return new CenterDistanceConstraint {
            TargetDistance = _targetDistance,
            SpringSettings = new SpringSettings(_springFrequency, _springDampingRatio)
        };
    }
}