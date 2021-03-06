using System;
using System.Windows.Forms;
using System.Drawing;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using PluginCore.DockPanelSuite;

namespace WeifenLuo.WinFormsUI.Docking
{
    public delegate string GetPersistStringCallback();

    public class DockContentHandler : IDisposable, IDockDragSource
    {
        public DockContentHandler(Form form) : this(form, null)
        {
        }

        public DockContentHandler(Form form, GetPersistStringCallback getPersistStringCallback)
        {
            if (!(form is IDockContent))
                throw new ArgumentException(Strings.DockContent_Constructor_InvalidForm, nameof(form));

            m_form = form;
            m_getPersistStringCallback = getPersistStringCallback;

            m_events = new EventHandlerList();
            Form.Disposed +=Form_Disposed;
            Form.TextChanged += Form_TextChanged;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                lock(this)
                {
                    DockPanel = null;
                    m_autoHideTab?.Dispose();
                    m_tab?.Dispose();

                    Form.Disposed -= Form_Disposed;
                    Form.TextChanged -= Form_TextChanged;
                    m_events.Dispose();
                }
            }
        }

        readonly Form m_form;
        public Form Form => m_form;

        public IDockContent Content => Form as IDockContent;

        IDockContent m_previousActive = null;
        public IDockContent PreviousActive
        {
            get => m_previousActive;
            internal set => m_previousActive = value;
        }

        IDockContent m_nextActive = null;
        public IDockContent NextActive
        {
            get => m_nextActive;
            internal set => m_nextActive = value;
        }

        readonly EventHandlerList m_events;
        EventHandlerList Events => m_events;

        bool m_allowEndUserDocking = true;
        public bool AllowEndUserDocking
        {
            get => m_allowEndUserDocking;
            set => m_allowEndUserDocking = value;
        }

        double m_autoHidePortion = 0.25;
        public double AutoHidePortion
        {
            get => m_autoHidePortion;
            set
            {
                if (value <= 0) throw(new ArgumentOutOfRangeException(Strings.DockContentHandler_AutoHidePortion_OutOfRange));
                if (value == m_autoHidePortion) return;
                m_autoHidePortion = value;
                if (DockPanel is null) return;
                if (DockPanel.ActiveAutoHideContent == Content)
                    DockPanel.PerformLayout();
            }
        }

        bool m_closeButton = true;
        public bool CloseButton
        {
            get => m_closeButton;
            set
            {
                if (value == m_closeButton) return;
                m_closeButton = value;
                if (Pane != null && Pane.ActiveContent.DockHandler == this) Pane.RefreshChanges();
            }
        }

        DockState DefaultDockState
        {
            get
            {
                if (ShowHint != DockState.Unknown && ShowHint != DockState.Hidden)
                    return ShowHint;

                if ((DockAreas & DockAreas.Document) != 0)
                    return DockState.Document;
                if ((DockAreas & DockAreas.DockRight) != 0)
                    return DockState.DockRight;
                if ((DockAreas & DockAreas.DockLeft) != 0)
                    return DockState.DockLeft;
                if ((DockAreas & DockAreas.DockBottom) != 0)
                    return DockState.DockBottom;
                if ((DockAreas & DockAreas.DockTop) != 0)
                    return DockState.DockTop;

                return DockState.Unknown;
            }
        }

        DockState DefaultShowState
        {
            get
            {
                if (ShowHint != DockState.Unknown)
                    return ShowHint;

                if ((DockAreas & DockAreas.Document) != 0)
                    return DockState.Document;
                if ((DockAreas & DockAreas.DockRight) != 0)
                    return DockState.DockRight;
                if ((DockAreas & DockAreas.DockLeft) != 0)
                    return DockState.DockLeft;
                if ((DockAreas & DockAreas.DockBottom) != 0)
                    return DockState.DockBottom;
                if ((DockAreas & DockAreas.DockTop) != 0)
                    return DockState.DockTop;
                if ((DockAreas & DockAreas.Float) != 0)
                    return DockState.Float;

                return DockState.Unknown;
            }
        }

        DockAreas m_allowedAreas = DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Document | DockAreas.Float;
        public DockAreas DockAreas
        {
            get => m_allowedAreas;
            set
            {
                if (m_allowedAreas == value)
                    return;

                if (!DockHelper.IsDockStateValid(DockState, value))
                    throw(new InvalidOperationException(Strings.DockContentHandler_DockAreas_InvalidValue));

                m_allowedAreas = value;

                if (!DockHelper.IsDockStateValid(ShowHint, m_allowedAreas))
                    ShowHint = DockState.Unknown;
            }
        }

        DockState m_dockState = DockState.Unknown;
        public DockState DockState
        {
            get => m_dockState;
            set
            {
                if (m_dockState == value)
                    return;

                DockPanel.SuspendLayout(true);

                if (value == DockState.Hidden)
                    IsHidden = true;
                else
                    SetDockState(false, value, Pane);

                DockPanel.ResumeLayout(true, true);
            }
        }

        DockPanel m_dockPanel = null;
        public DockPanel DockPanel
        {
            get => m_dockPanel;
            set
            {
                if (m_dockPanel == value)
                    return;

                Pane = null;

                m_dockPanel?.RemoveContent(Content);

                if (m_tab != null)
                {
                    m_tab.Dispose();
                    m_tab = null;
                }

                if (m_autoHideTab != null)
                {
                    m_autoHideTab.Dispose();
                    m_autoHideTab = null;
                }

                m_dockPanel = value;

                if (m_dockPanel != null)
                {
                    m_dockPanel.AddContent(Content);
                    Form.TopLevel = false;
                    Form.FormBorderStyle = FormBorderStyle.None;
                    Form.ShowInTaskbar = false;
                    Form.WindowState = FormWindowState.Normal;

                    if (!NativeMethods.ShouldUseWin32()) return;

                    NativeMethods.SetWindowPos(Form.Handle, IntPtr.Zero, 0, 0, 0, 0,
                        Win32.FlagsSetWindowPos.SWP_NOACTIVATE |
                        Win32.FlagsSetWindowPos.SWP_NOMOVE |
                        Win32.FlagsSetWindowPos.SWP_NOSIZE |
                        Win32.FlagsSetWindowPos.SWP_NOZORDER |
                        Win32.FlagsSetWindowPos.SWP_NOOWNERZORDER |
                        Win32.FlagsSetWindowPos.SWP_FRAMECHANGED);
                }
            }
        }

        public Icon Icon => Form.Icon;

        public DockPane Pane
        {
            get => IsFloat ? FloatPane : PanelPane;
            set
            {
                if (Pane == value)
                    return;

                DockPanel.SuspendLayout(true);

                DockPane oldPane = Pane;

                SuspendSetDockState();
                FloatPane = (value is null ? null : (value.IsFloat ? value : FloatPane));
                PanelPane = (value is null ? null : (value.IsFloat ? PanelPane : value));
                ResumeSetDockState(IsHidden, value?.DockState ?? DockState.Unknown, oldPane);

                DockPanel.ResumeLayout(true, true);
            }
        }

        bool m_isHidden = true;
        public bool IsHidden
        {
            get => m_isHidden;
            set
            {
                if (m_isHidden == value)
                    return;

                SetDockState(value, VisibleState, Pane);
            }
        }

        string m_tabText = null;
        public string TabText
        {
            get => m_tabText ?? Form.Text;
            set
            {
                if (m_tabText == value)
                    return;

                m_tabText = value;
                Pane?.RefreshChanges();
            }
        }

        Color m_tabColor = Color.Transparent;
        public Color TabColor
        {
            get => m_tabColor == Color.Transparent ? Color.Transparent : m_tabColor;
            set
            {
                if (m_tabColor == value)
                    return;

                m_tabColor = value;
                Pane?.RefreshChanges();
            }
        }

        DockState m_visibleState = DockState.Unknown;
        public DockState VisibleState
        {
            get => m_visibleState;
            set
            {
                if (m_visibleState == value)
                    return;

                SetDockState(IsHidden, value, Pane);
            }
        }

        bool m_isFloat = false;
        public bool IsFloat
        {
            get => m_isFloat;
            set
            {
                if (m_isFloat == value)
                    return;

                DockState visibleState = CheckDockState(value);

                if (visibleState == DockState.Unknown)
                    throw new InvalidOperationException(Strings.DockContentHandler_IsFloat_InvalidValue);

                SetDockState(IsHidden, visibleState, Pane);
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA1720:AvoidTypeNamesInParameters")]
        public DockState CheckDockState(bool isFloat)
        {
            DockState dockState;

            if (isFloat)
            {
                if (!IsDockStateValid(DockState.Float))
                    dockState = DockState.Unknown;
                else
                    dockState = DockState.Float;
            }
            else
            {
                dockState = PanelPane?.DockState ?? DefaultDockState;
                if (dockState != DockState.Unknown && !IsDockStateValid(dockState))
                {
                    dockState = DockState.Unknown;
                }
            }
            return dockState;
        }

        DockPane m_panelPane = null;
        public DockPane PanelPane
        {
            get => m_panelPane;
            set
            {
                if (m_panelPane == value)
                    return;

                if (value != null)
                {
                    if (value.IsFloat || value.DockPanel != DockPanel)
                        throw new InvalidOperationException(Strings.DockContentHandler_DockPane_InvalidValue);
                }

                DockPane oldPane = Pane;

                if (m_panelPane != null)
                    RemoveFromPane(m_panelPane);
                m_panelPane = value;
                if (m_panelPane != null)
                {
                    m_panelPane.AddContent(Content);
                    SetDockState(IsHidden, IsFloat ? DockState.Float : m_panelPane.DockState, oldPane);
                }
                else
                    SetDockState(IsHidden, DockState.Unknown, oldPane);
            }
        }

        void RemoveFromPane(DockPane pane)
        {
            pane.RemoveContent(Content);
            SetPane(null);
            if (pane.Contents.Count == 0)
                pane.Dispose();
        }

        DockPane m_floatPane = null;
        public DockPane FloatPane
        {
            get => m_floatPane;
            set
            {
                if (m_floatPane == value)
                    return;

                if (value != null)
                {
                    if (!value.IsFloat || value.DockPanel != DockPanel)
                        throw new InvalidOperationException(Strings.DockContentHandler_FloatPane_InvalidValue);
                }

                DockPane oldPane = Pane;

                if (m_floatPane != null)
                    RemoveFromPane(m_floatPane);
                m_floatPane = value;
                if (m_floatPane != null)
                {
                    m_floatPane.AddContent(Content);
                    SetDockState(IsHidden, IsFloat ? DockState.Float : VisibleState, oldPane);
                }
                else
                    SetDockState(IsHidden, DockState.Unknown, oldPane);
            }
        }

        int m_countSetDockState = 0;

        void SuspendSetDockState()
        {
            m_countSetDockState ++;
        }

        void ResumeSetDockState()
        {
            m_countSetDockState --;
            if (m_countSetDockState < 0)
                m_countSetDockState = 0;
        }

        internal bool IsSuspendSetDockState => m_countSetDockState != 0;

        void ResumeSetDockState(bool isHidden, DockState visibleState, DockPane oldPane)
        {
            ResumeSetDockState();
            SetDockState(isHidden, visibleState, oldPane);
        }

        internal void SetDockState(bool isHidden, DockState visibleState, DockPane oldPane)
        {
            if (IsSuspendSetDockState)
                return;

            if (DockPanel is null && visibleState != DockState.Unknown)
                throw new InvalidOperationException(Strings.DockContentHandler_SetDockState_NullPanel);

            if (visibleState == DockState.Hidden || (visibleState != DockState.Unknown && !IsDockStateValid(visibleState)))
                throw new InvalidOperationException(Strings.DockContentHandler_SetDockState_InvalidState);

            DockPanel dockPanel = DockPanel;
            dockPanel?.SuspendLayout(true);

            SuspendSetDockState();

            DockState oldDockState = DockState;

            if (m_isHidden != isHidden || oldDockState == DockState.Unknown)
            {
                m_isHidden = isHidden;
            }
            m_visibleState = visibleState;
            m_dockState = isHidden ? DockState.Hidden : visibleState;

            if (visibleState == DockState.Unknown)
                Pane = null;
            else
            {
                m_isFloat = (m_visibleState == DockState.Float);

                if (Pane is null)
                    Pane = DockPanel.DockPaneFactory.CreateDockPane(Content, visibleState, true);
                else if (Pane.DockState != visibleState)
                {
                    if (Pane.Contents.Count == 1)
                        Pane.SetDockState(visibleState);
                    else
                        Pane = DockPanel.DockPaneFactory.CreateDockPane(Content, visibleState, true);
                }
            }

            if (Form.ContainsFocus)
            {
                if (DockState == DockState.Hidden || DockState == DockState.Unknown)
                {
                    if (NativeMethods.ShouldUseWin32())
                    {
                        DockPanel.ContentFocusManager.GiveUpFocus(Content);
                    }
                }
            }
            SetPaneAndVisible(Pane);

            if (oldPane != null && !oldPane.IsDisposed && oldDockState == oldPane.DockState) RefreshDockPane(oldPane);

            if (Pane != null && DockState == Pane.DockState)
            {
                if ((Pane != oldPane) || (Pane == oldPane && oldDockState != oldPane.DockState))
                {
                    RefreshDockPane(Pane);
                }
            }

            if (oldDockState != DockState)
            {
                if (DockState == DockState.Hidden || DockState == DockState.Unknown || DockHelper.IsDockStateAutoHide(DockState))
                {
                    if (NativeMethods.ShouldUseWin32())
                    {
                        DockPanel.ContentFocusManager.RemoveFromList(Content);
                    }
                }
                else if (NativeMethods.ShouldUseWin32())
                {
                    DockPanel.ContentFocusManager.AddToList(Content);
                }
                OnDockStateChanged(EventArgs.Empty);
            }
            ResumeSetDockState();

            dockPanel?.ResumeLayout(true, true);
        }

        static void RefreshDockPane(DockPane pane)
        {
            pane.RefreshChanges();
            pane.ValidateActiveContent();
        }

        internal string PersistString => GetPersistStringCallback is null ? Form.GetType().ToString() : GetPersistStringCallback();

        GetPersistStringCallback m_getPersistStringCallback = null;
        public GetPersistStringCallback GetPersistStringCallback
        {
            get => m_getPersistStringCallback;
            set => m_getPersistStringCallback = value;
        }


        bool m_hideOnClose = false;
        public bool HideOnClose
        {
            get => m_hideOnClose;
            set => m_hideOnClose = value;
        }

        DockState m_showHint = DockState.Unknown;
        public DockState ShowHint
        {
            get => m_showHint;
            set
            {   
                if (!DockHelper.IsDockStateValid(value, DockAreas))
                    throw (new InvalidOperationException(Strings.DockContentHandler_ShowHint_InvalidValue));

                if (m_showHint == value)
                    return;

                m_showHint = value;
            }
        }

        bool m_isActivated = false;
        public bool IsActivated => m_isActivated;

        internal void SetIsActivated(bool value)
        {
            if (m_isActivated == value)
                return;

            m_isActivated = value;
            OnIsActivatedChanged(EventArgs.Empty);
        }

        public bool IsDockStateValid(DockState dockState)
        {
            if (DockPanel != null && dockState == DockState.Document && DockPanel.DocumentStyle == DocumentStyle.SystemMdi)
                return false;
            return DockHelper.IsDockStateValid(dockState, DockAreas);
        }

        ContextMenu m_tabPageContextMenu = null;
        public ContextMenu TabPageContextMenu
        {
            get => m_tabPageContextMenu;
            set => m_tabPageContextMenu = value;
        }

        string m_toolTipText = null;
        public string ToolTipText
        {
            get => m_toolTipText;
            set => m_toolTipText = value;
        }

        public void Activate()
        {
            if (DockPanel is null) Form.Activate();
            else if (Pane is null) Show(DockPanel);
            else
            {
                IsHidden = false;
                Pane.ActiveContent = Content;
                if (DockState == DockState.Document && DockPanel.DocumentStyle == DocumentStyle.SystemMdi)
                {
                    Form.Activate();
                    return;
                }

                if (DockHelper.IsDockStateAutoHide(DockState)) DockPanel.ActiveAutoHideContent = Content;

                if (!Form.ContainsFocus)
                {
                    if (NativeMethods.ShouldUseWin32()) DockPanel.ContentFocusManager.Activate(Content);
                }
            }
        }

        public void GiveUpFocus()
        {
            if (NativeMethods.ShouldUseWin32()) DockPanel.ContentFocusManager.GiveUpFocus(Content);
        }

        IntPtr m_activeWindowHandle = IntPtr.Zero;
        internal IntPtr ActiveWindowHandle
        {
            get => m_activeWindowHandle;
            set => m_activeWindowHandle = value;
        }

        public void Hide()
        {
            IsHidden = true;
        }

        internal void SetPaneAndVisible(DockPane pane)
        {
            SetPane(pane);
            SetVisible();
        }

        void SetPane(DockPane pane)
        {
            if (pane != null && pane.DockState == DockState.Document && DockPanel.DocumentStyle == DocumentStyle.DockingMdi)
            {
                if (Form.Parent is DockPane)
                    SetParent(null);
                if (Form.MdiParent != DockPanel.ParentForm)
                {
                    FlagClipWindow = true;
                    Form.MdiParent = DockPanel.ParentForm;
                }
            }
            else
            {
                FlagClipWindow = true;
                if (Form.MdiParent != null)
                    Form.MdiParent = null;
                if (Form.TopLevel)
                    Form.TopLevel = false;
                SetParent(pane);
            }
        }

        internal void SetVisible()
        {
            bool visible;

            if (IsHidden)
                visible = false;
            else if (Pane != null && Pane.DockState == DockState.Document && DockPanel.DocumentStyle == DocumentStyle.DockingMdi)
                visible = true;
            else if (Pane != null && Pane.ActiveContent == Content)
                visible = true;
            else if (Pane != null && Pane.ActiveContent != Content)
                visible = false;
            else
                visible = Form.Visible;

            if (Form.Visible != visible)
                Form.Visible = visible;
        }

        void SetParent(Control value)
        {
            if (Form.Parent == value)
                return;

            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            // Workaround of .Net Framework bug:
            // Change the parent of a control with focus may result in the first
            // MDI child form get activated. 
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            bool bRestoreFocus = false;
            if (Form.ContainsFocus)
            {
                // Suggested as a fix for a memory leak by bugreports
                if (value is null && !IsFloat)
                {
                    if (NativeMethods.ShouldUseWin32())
                    {
                        DockPanel.ContentFocusManager.GiveUpFocus(this.Content);
                    }
                    else
                    {
                        DockPanel.SaveFocus();
                        bRestoreFocus = true;
                    }
                }
            }
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

            Form.Parent = value;

            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            // Workaround of .Net Framework bug:
            // Change the parent of a control with focus may result in the first
            // MDI child form get activated. 
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            if (bRestoreFocus)
                Activate();
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        public void Show()
        {
            if (DockPanel is null)
                Form.Show();
            else
                Show(DockPanel);
        }

        public void Show(DockPanel dockPanel)
        {
            if (dockPanel is null)
                throw(new ArgumentNullException(Strings.DockContentHandler_Show_NullDockPanel));

            if (DockState == DockState.Unknown) Show(dockPanel, DefaultShowState);
            else Activate();
        }

        public void Show(DockPanel dockPanel, DockState dockState)
        {
            if (dockPanel is null)
                throw(new ArgumentNullException(Strings.DockContentHandler_Show_NullDockPanel));

            if (dockState == DockState.Unknown || dockState == DockState.Hidden)
                throw(new ArgumentException(Strings.DockContentHandler_Show_InvalidDockState));

            dockPanel.SuspendLayout(true);

            DockPanel = dockPanel;

            if (dockState == DockState.Float && FloatPane is null)
                Pane = DockPanel.DockPaneFactory.CreateDockPane(Content, DockState.Float, true);
            else if (PanelPane is null)
            {
                DockPane paneExisting = null;
                foreach (DockPane pane in DockPanel.Panes)
                    if (pane.DockState == dockState)
                    {
                        paneExisting = pane;
                        break;
                    }

                if (paneExisting is null)
                    Pane = DockPanel.DockPaneFactory.CreateDockPane(Content, dockState, true);
                else
                    Pane = paneExisting;
            }

            DockState = dockState;
            Activate();

            dockPanel.ResumeLayout(true, true);
        }

        [SuppressMessage("Microsoft.Naming", "CA1720:AvoidTypeNamesInParameters")]
        public void Show(DockPanel dockPanel, Rectangle floatWindowBounds)
        {
            if (dockPanel is null)
                throw(new ArgumentNullException(Strings.DockContentHandler_Show_NullDockPanel));

            dockPanel.SuspendLayout(true);

            DockPanel = dockPanel;
            if (FloatPane is null)
            {
                IsHidden = true;    // to reduce the screen flicker
                FloatPane = DockPanel.DockPaneFactory.CreateDockPane(Content, DockState.Float, false);
                FloatPane.FloatWindow.StartPosition = FormStartPosition.Manual;
            }

            FloatPane.FloatWindow.Bounds = floatWindowBounds;
            
            Show(dockPanel, DockState.Float);
            Activate();

            dockPanel.ResumeLayout(true, true);
        }

        public void Show(DockPane pane, IDockContent beforeContent)
        {
            if (pane is null)
                throw(new ArgumentNullException(Strings.DockContentHandler_Show_NullPane));

            if (beforeContent != null && pane.Contents.IndexOf(beforeContent) == -1)
                throw(new ArgumentException(Strings.DockContentHandler_Show_InvalidBeforeContent));

            pane.DockPanel.SuspendLayout(true);

            DockPanel = pane.DockPanel;
            Pane = pane;
            pane.SetContentIndex(Content, pane.Contents.IndexOf(beforeContent));
            Show();

            pane.DockPanel.ResumeLayout(true, true);
        }

        public void Show(DockPane previousPane, DockAlignment alignment, double proportion)
        {
            if (previousPane is null)
                throw(new ArgumentException(Strings.DockContentHandler_Show_InvalidPrevPane));

            if (DockHelper.IsDockStateAutoHide(previousPane.DockState))
                throw(new ArgumentException(Strings.DockContentHandler_Show_InvalidPrevPane));

            previousPane.DockPanel.SuspendLayout(true);

            DockPanel = previousPane.DockPanel;
            DockPanel.DockPaneFactory.CreateDockPane(Content, previousPane, alignment, proportion, true);
            Show();

            previousPane.DockPanel.ResumeLayout(true, true);
        }

        public void Close()
        {
            DockPanel dockPanel = DockPanel;
            dockPanel?.SuspendLayout(true);
            Form.Close();
            dockPanel?.ResumeLayout(true, true);

        }

        DockPaneStripBase.Tab m_tab = null;
        internal DockPaneStripBase.Tab GetTab(DockPaneStripBase dockPaneStrip)
        {
            if (m_tab is null)
                m_tab = dockPaneStrip.CreateTab(Content);

            return m_tab;
        }

        IDisposable m_autoHideTab = null;
        internal IDisposable AutoHideTab
        {
            get => m_autoHideTab;
            set => m_autoHideTab = value;
        }

        #region Events

        static readonly object DockStateChangedEvent = new object();
        public event EventHandler DockStateChanged
        {
            add => Events.AddHandler(DockStateChangedEvent, value);
            remove => Events.RemoveHandler(DockStateChangedEvent, value);
        }
        protected virtual void OnDockStateChanged(EventArgs e)
        {
            EventHandler handler = (EventHandler)Events[DockStateChangedEvent];
            handler?.Invoke(this, e);
        }

        static readonly object IsActivatedChangedEvent = new object();
        public event EventHandler IsActivatedChanged
        {
            add => Events.AddHandler(IsActivatedChangedEvent, value);
            remove => Events.RemoveHandler(IsActivatedChangedEvent, value);
        }
        protected virtual void OnIsActivatedChanged(EventArgs e)
        {
            EventHandler handler = (EventHandler) Events[IsActivatedChangedEvent];
            handler?.Invoke(this, e);
        }
        #endregion

        void Form_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        void Form_TextChanged(object sender, EventArgs e)
        {
            if (DockHelper.IsDockStateAutoHide(DockState))
                DockPanel.RefreshAutoHideStrip();
            else if (Pane != null)
            {
                Pane.FloatWindow?.SetText();
                Pane.RefreshChanges();
            }
        }

        bool m_flagClipWindow = false;
        internal bool FlagClipWindow
        {
            get => m_flagClipWindow;
            set
            {
                if (m_flagClipWindow == value)
                    return;

                m_flagClipWindow = value;
                if (m_flagClipWindow)
                    Form.Region = new Region(Rectangle.Empty);
                else
                    Form.Region = null;
            }
        }

        ContextMenuStrip m_tabPageContextMenuStrip = null;
        public ContextMenuStrip TabPageContextMenuStrip
        {
            get => m_tabPageContextMenuStrip;
            set => m_tabPageContextMenuStrip = value;
        }

        #region IDockDragSource Members

        Control IDragSource.DragControl => Form;

        bool IDockDragSource.CanDockTo(DockPane pane)
        {
            if (!IsDockStateValid(pane.DockState))
                return false;

            if (Pane == pane && pane.DisplayingContents.Count == 1)
                return false;

            return true;
        }

        Rectangle IDockDragSource.BeginDrag(Point ptMouse)
        {
            Size size;
            DockPane floatPane = this.FloatPane;
            if (DockState == DockState.Float || floatPane is null || floatPane.FloatWindow.NestedPanes.Count != 1)
                size = DockPanel.DefaultFloatWindowSize;
            else
                size = floatPane.FloatWindow.Size;

            Point location;
            Rectangle rectPane = Pane.ClientRectangle;
            if (DockState == DockState.Document)
                location = new Point(rectPane.Left, rectPane.Top);
            else
            {
                location = new Point(rectPane.Left, rectPane.Bottom);
                location.Y -= size.Height;
            }
            location = Pane.PointToScreen(location);

            if (ptMouse.X > location.X + size.Width)
                location.X += ptMouse.X - (location.X + size.Width) + Measures.SplitterSize;

            return new Rectangle(location, size);
        }

        public void FloatAt(Rectangle floatWindowBounds)
        {
            DockPanel.DockPaneFactory.CreateDockPane(Content, floatWindowBounds, true);
        }

        public void DockTo(DockPane pane, DockStyle dockStyle, int contentIndex)
        {
            DockTo(pane, dockStyle, contentIndex, 0.5);
        }

        public void DockTo(DockPane pane, DockStyle dockStyle, int contentIndex, double proportion)
        {
            if (dockStyle == DockStyle.Fill)
            {
                bool samePane = (Pane == pane);
                if (!samePane)
                    Pane = pane;

                if (contentIndex == -1 || !samePane)
                    pane.SetContentIndex(Content, contentIndex);
                else
                {
                    DockContentCollection contents = pane.Contents;
                    int oldIndex = contents.IndexOf(Content);
                    int newIndex = contentIndex;
                    if (oldIndex < newIndex)
                    {
                        newIndex += 1;
                        if (newIndex > contents.Count -1)
                            newIndex = -1;
                    }
                    pane.SetContentIndex(Content, newIndex);
                }
            }
            else
            {
                DockPane paneFrom = DockPanel.DockPaneFactory.CreateDockPane(Content, pane.DockState, true);
                INestedPanesContainer container = pane.NestedPanesContainer;
                if (dockStyle == DockStyle.Left)
                    paneFrom.DockTo(container, pane, DockAlignment.Left, proportion);
                else if (dockStyle == DockStyle.Right)
                    paneFrom.DockTo(container, pane, DockAlignment.Right, proportion);
                else if (dockStyle == DockStyle.Top)
                    paneFrom.DockTo(container, pane, DockAlignment.Top, proportion);
                else if (dockStyle == DockStyle.Bottom)
                    paneFrom.DockTo(container, pane, DockAlignment.Bottom, proportion);

                paneFrom.DockState = pane.DockState;
            }
        }

        public void DockTo(DockPanel panel, DockStyle dockStyle)
        {
            if (panel != DockPanel)
                throw new ArgumentException(Strings.IDockDragSource_DockTo_InvalidPanel, nameof(panel));

            DockPane pane;

            if (dockStyle == DockStyle.Top)
                pane = DockPanel.DockPaneFactory.CreateDockPane(Content, DockState.DockTop, true);
            else if (dockStyle == DockStyle.Bottom)
                pane = DockPanel.DockPaneFactory.CreateDockPane(Content, DockState.DockBottom, true);
            else if (dockStyle == DockStyle.Left)
                pane = DockPanel.DockPaneFactory.CreateDockPane(Content, DockState.DockLeft, true);
            else if (dockStyle == DockStyle.Right)
                pane = DockPanel.DockPaneFactory.CreateDockPane(Content, DockState.DockRight, true);
            else if (dockStyle == DockStyle.Fill)
                pane = DockPanel.DockPaneFactory.CreateDockPane(Content, DockState.Document, true);
            else
                return;
        }

        #endregion
    }
}