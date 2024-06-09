﻿using Prowl.Editor.Assets;
using Prowl.Editor.Docking;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Editor
{
    public class EditorWindow
    {
        protected string Title = "Title";
        internal readonly int _id;

        protected virtual bool Center { get; } = false;
        protected virtual double Width { get; } = 256;
        protected virtual double Height { get; } = 256;
        protected virtual bool TitleBar { get; } = true;
        protected virtual bool IsDockable { get; } = true;
        protected virtual bool LockSize { get; } = false;
        protected virtual bool BackgroundFade { get; } = false;
        protected virtual bool RoundCorners { get; } = true;
        protected virtual double Padding { get; } = 8;

        protected bool isOpened = true;
        protected Runtime.GUI.Gui gui => Runtime.GUI.Gui.ActiveGUI;

        private double _width, _height;
        public double _x, _y;
        private bool _wasDragged = false;


        public bool bAllowTabs = true;

        public Vector2 DockPosition;
        public Vector2 DockSize;

        public double MinZ = double.MaxValue;
        public double MaxZ = double.MinValue;

        public bool IsDocked => m_Leaf != null;
        private DockNode m_Leaf;
        private Vector2 m_DockPosition;

        public DockNode Leaf {
            get => m_Leaf;
            internal set => m_Leaf = value;
        }
        public Rect Rect {
            get;
            private set;
        }

        public bool IsFocused => EditorGuiManager.FocusedWindow != null && EditorGuiManager.FocusedWindow.Target == this;

        public EditorWindow() : base()
        {
            EditorGuiManager.Windows.Add(this);
            _id = GetHashCode();

            _width = Width;
            _height = Height;
        }

        public void ProcessFrame()
        {
            MinZ = gui.GetCurrentInteractableZLayer();

            try
            {
                Update();
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in UpdateWindow: " + e.Message + "\n" + e.StackTrace);
            }

            try
            {
                isOpened = true;

                if (BackgroundFade)
                {
                    gui.BlockInteractables(gui.ScreenRect);
                    gui.Draw2D.DrawRectFilled(gui.ScreenRect, new System.Numerics.Vector4(0, 0, 0, 0.5f));
                    // Ensure were at the start of the EditorWindows List
                    EditorGuiManager.FocusWindow(this);

                }

                if (Center)
                {
                    var vp_size = gui.ScreenRect.Size / 2;
                    _x = vp_size.x - (_width / 2);
                    _y = vp_size.y - (_height / 2);
                }

                var width = _width;
                var height = _height;
                if (IsDocked)
                {
                    _x = DockPosition.x;
                    _y = DockPosition.y;
                    // Dock is Relative to Node, Convert to Screen Space
                    _x -= gui.CurrentNode.LayoutData.Rect.x;
                    _y -= gui.CurrentNode.LayoutData.Rect.y;
                    width = DockSize.x;
                    height = DockSize.y;
                }

                using (gui.Node("_" + Title, _id).Width(width).Height(height).Padding(Padding).Left(_x).Top(_y).Layout(LayoutType.Column).ScaleChildren().Enter())
                {
                    gui.BlockInteractables(gui.CurrentNode.LayoutData.InnerRect);
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.InnerRect, GuiStyle.WindowBackground, 10);

                    Rect = gui.CurrentNode.LayoutData.InnerRect;

                    if (!LockSize && !IsDocked)
                        HandleResize();

                    if (TitleBar)
                    {
                        using (gui.Node("_Titlebar").Width(Size.Percentage(1f)).MaxHeight(40).Padding(10, 10).Enter())
                        {
                            HandleTitleBarInteraction();

                            if (IsDocked && Leaf.LeafWindows.Count > 0)
                            {
                                double[] tabWidths = new double[Leaf.LeafWindows.Count];
                                double total = 0;
                                for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                                {
                                    var window = Leaf.LeafWindows[i];
                                    var textSize = UIDrawList.DefaultFont.CalcTextSize(window.Title, 0);
                                    tabWidths[i] = textSize.x + 20;
                                    total += tabWidths[i];
                                }

                                double updatedTotal = 0;
                                if (total > gui.CurrentNode.LayoutData.InnerRect.width - 35)
                                {
                                    for (int i = 0; i < tabWidths.Length; i++)
                                    {
                                        tabWidths[i] = (tabWidths[i] / total) * (gui.CurrentNode.LayoutData.InnerRect.width - 35);
                                        updatedTotal += tabWidths[i];
                                    }
                                }
                                else
                                {
                                    updatedTotal = total;
                                }

                                // background rect for all tabs
                                if (Leaf.LeafWindows.Count > 1)
                                {
                                    var tabsRect = gui.CurrentNode.LayoutData.InnerRect;
                                    tabsRect.x += 2;
                                    tabsRect.width = updatedTotal;
                                    tabsRect.Expand(6);
                                    gui.Draw2D.DrawRectFilled(tabsRect, GuiStyle.WindowBackground * 0.8f, 10);
                                }

                                double left = 0;
                                for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                                {
                                    var window = Leaf.LeafWindows[i];
                                    var tabWidth = tabWidths[i];
                                    using (gui.Node("Tab _" + window.Title, window._id).Width(tabWidth).Height(20).Left(left).Enter())
                                    {
                                        left += tabWidth + 5;
                                        var tabRect = gui.CurrentNode.LayoutData.Rect;
                                        tabRect.Expand(0, 2);

                                        if (window != this)
                                        {
                                            if (gui.IsNodePressed())
                                            {
                                                Leaf.WindowNum = i;
                                                EditorGuiManager.FocusWindow(window);
                                            }
                                            if (gui.IsNodeHovered())
                                                gui.Draw2D.DrawRectFilled(tabRect, GuiStyle.Borders, 10);
                                        }
                                        if (window == this)
                                        {
                                            gui.Draw2D.DrawRectFilled(tabRect, GuiStyle.Indigo, 10);
                                        }

                                        var textSize = UIDrawList.DefaultFont.CalcTextSize(window.Title, 0);
                                        var pos = gui.CurrentNode.LayoutData.Rect.Position;
                                        pos.x += (tabRect.width - textSize.x) * 0.5f;
                                        pos.y += (tabRect.height - (textSize.y)) * 0.5f;
                                        if (textSize.x < tabWidth - 10)
                                            gui.Draw2D.DrawText(UIDrawList.DefaultFont, window.Title, 20, pos, Color.white);
                                        else
                                            gui.Draw2D.DrawText(UIDrawList.DefaultFont, "...", 20, new Vector2(tabRect.x + (tabRect.width * 0.5) - 5, pos.y), Color.white);

                                        // Close Button
                                        if (gui.IsNodeHovered())
                                        {
                                            using (gui.Node("_CloseButton").Width(20).Height(20).Left(Offset.Percentage(1f, -23)).Enter())
                                            {
                                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, new Color(1, 1, 1, 150), 10);
                                                if (gui.IsPointerHovering() && gui.IsPointerClick())
                                                {
                                                    //Leaf.LeafWindows.Remove(window);
                                                    //EditorGuiManager.Remove(window);
                                                    //EditorGuiManager.FocusWindow(Leaf.LeafWindows[Leaf.WindowNum]);
                                                    if (window == this)
                                                        isOpened = false;
                                                    else
                                                        EditorGuiManager.Remove(window);
                                                }
                                                gui.Draw2D.DrawText(UIDrawList.DefaultFont, FontAwesome6.Xmark, 20, gui.CurrentNode.LayoutData.Rect, gui.IsPointerHovering() ? GuiStyle.Base11 : GuiStyle.Base9);
                                            }

                                        }
                                    }
                                }
                            }
                            else
                            {
                                gui.Draw2D.DrawText(UIDrawList.DefaultFont, Title, 20, gui.CurrentNode.LayoutData.Rect, Color.white);
                            }

                            DrawWindowManagementButton();
                        }


                        using (gui.Node("_Main").Width(Size.Percentage(1f)).Clip().Enter())
                        {
                            Draw();
                        }
                    }
                    else
                    {
                        Draw();
                    }
                    gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Borders, 2, 10);
                }


                if (!isOpened)
                {
                    if (IsDocked)
                        EditorGuiManager.Container.DetachWindow(this);
                    EditorGuiManager.Remove(this);
                    Close();
                }
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in EditorWindow: " + e.Message + "\n" + e.StackTrace);
            }

            MaxZ = gui.GetCurrentInteractableZLayer();
        }

        private void DrawWindowManagementButton()
        {
            using (gui.Node("_WindowManageBtn").Width(20).Height(20).Left(Offset.Percentage(1f, -20)).Enter())
            {
                if (gui.IsNodePressed())
                    gui.OpenPopup("WindowManagement");
                gui.Draw2D.DrawText(FontAwesome6.EllipsisVertical, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base6);

                if (gui.BeginPopup("WindowManagement", out var node))
                {
                    var popupHolder = gui.CurrentNode;
                    using (node.Width(150).Layout(LayoutType.Column).FitContentHeight().Enter())
                    {
                        bool closePopup = false;
                        if (EditorGUI.StyledButton("Duplicate"))
                        {
                            _ = (EditorWindow)Activator.CreateInstance(GetType());
                            closePopup = true;
                        }

                        if (EditorGUI.StyledButton("Close All"))
                        {
                            if(!IsDocked)
                                EditorGuiManager.Remove(this);
                            else
                            {
                                foreach (var window in Leaf.LeafWindows)
                                {
                                    EditorGuiManager.Remove(window);
                                }
                            }
                            closePopup = true;
                        }

                        EditorGUI.Separator();

                        if (EditorGUI.StyledButton("Scene View"))            { new SceneViewWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Game"))                  { new GameWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Hierarchy"))             { new HierarchyWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Inspector"))             { new InspectorWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Asset Browser"))         { new AssetsBrowserWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Asset Tree"))            { new AssetsTreeWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Console"))               { new ConsoleWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Project Settings"))      { new ProjectSettingsWindow(); closePopup = true; }
                        if (EditorGUI.StyledButton("Editor Preferences"))    { new PreferencesWindow(); closePopup = true; }

                        if (closePopup)
                            gui.ClosePopup(popupHolder);
                    }
                }
            }

            if (!IsDocked)
            {
                // If the window isnt docked then theres no tab with a Close button
                // So we need to draw the close button on the title bar instead
                using (gui.Node("_CloseButton").Width(20).Height(20).Left(Offset.Percentage(1f, -45)).Enter())
                {
                    if (gui.IsNodePressed())
                        isOpened = false;
                    gui.Draw2D.DrawText(FontAwesome6.Xmark, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base6);
                }
            }
        }

        private bool _wasResizing = false;
        private void HandleResize()
        {
            using (gui.Node("ResizeTab").TopLeft(Offset.Percentage(1f, -15)).Scale(15).IgnoreLayout().Enter())
            {
                if (gui.IsNodePressed() || gui.IsNodeActive())
                {
                    if (!_wasResizing)
                    {
                        _wasResizing = true;
                    }
                    else
                    {
                        _width += gui.PointerDelta.x;
                        _height += gui.PointerDelta.y;

                        // If width or height is less than 10, move the window instead
                        if (_width < 150)
                            _width = 150;
                        if (_height < 150)
                            _height = 150;
                    }
                }
                else
                {
                    _wasResizing = false;
                }

                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? new(1f, 1f, 1f, 0.5f) : new(1f, 1f, 1f, 0.2f), 10, 5);
            }
        }

        private void HandleTitleBarInteraction()
        {
            var titleInteract = gui.GetInteractable();
            if (EditorGuiManager.DragSplitter == null)
            {
                if (titleInteract.TakeFocus() || titleInteract.IsActive())
                {
                    EditorGuiManager.FocusWindow(this);
                    if (_wasDragged || gui.IsPointerMoving)
                    {
                        _wasDragged = true;

                        _x += gui.PointerDelta.x;
                        _y += gui.PointerDelta.y;
                        EditorGuiManager.DraggingWindow = this;

                        if (gui.IsPointerMoving && IsDocked)
                        {
                            EditorGuiManager.Container.DetachWindow(this);
                            // Position the window so the mouse is over the title bar
                            _x = gui.PointerPos.x - (_width / 2);
                            _y = gui.PointerPos.y - 10;
                        }

                        if (IsDockable && !IsDocked)
                        {
                            // Draw Docking Placement
                            var oldZ = gui.CurrentZIndex;
                            gui.SetZIndex(10000);
                            _ = EditorGuiManager.Container.GetPlacement(gui.PointerPos.x, gui.PointerPos.y, out var placements, out var hovered);
                            if (placements != null)
                            {
                                foreach (var possible in placements)
                                {
                                    gui.Draw2D.DrawRectFilled(possible, GuiStyle.Blue * 0.6f, 10);
                                    gui.Draw2D.DrawRect(possible, GuiStyle.Blue * 0.6f, 4, 10);
                                }
                                gui.Draw2D.DrawRect(hovered, Color.yellow, 4, 10);
                            }
                            gui.SetZIndex(oldZ);
                        }
                    }
                }
                else
                {
                    if (_wasDragged)
                    {
                        _wasDragged = false;
                        if (IsDockable && !IsDocked)
                        {
                            Vector2 cursorPos = gui.PointerPos;
                            EditorGuiManager.Container.AttachWindowAt(this, cursorPos.x, cursorPos.y);
                        }
                    }

                    if (EditorGuiManager.DraggingWindow == this)
                    {
                        EditorGuiManager.DraggingWindow = null;
                    }
                }
            }
        }

        protected virtual void Draw() { }
        protected virtual void Update() { }
        protected virtual void Close() { }

    }
}