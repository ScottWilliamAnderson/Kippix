using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace KippixLinks
{
    /// <summary>
    /// Links: a "Links" button in Kippix's top bar (fullscreen) and a Links entry in Playnite's main menu (either mode).
    /// It keeps the web pages you add and opens them full screen in your browser.
    /// Built with the .NET Framework C# 5 compiler (csc.exe): no newer syntax.
    /// </summary>
    public class LinksPlugin : GenericPlugin
    {
        private static readonly Guid PluginId = new Guid("08ee0e70-aeb1-4ad0-a0d7-14f23832c57d");
        public override Guid Id { get { return PluginId; } }

        private readonly IPlayniteAPI api;
        private readonly KippixSeam seam;
        private readonly Handoff handoff = new Handoff();
        private LinkStore store;
        private PanelSettings settings;
        private Opener opener;
        private TopBarButton topBar;
        private bool panelOpen;
        private LinksPanel openPanel;

        public LinksPlugin(IPlayniteAPI api) : base(api)
        {
            this.api = api;
            Properties = new GenericPluginProperties { HasSettings = false };
            seam = new KippixSeam(api);
        }

        private string DataFolder { get { return GetPluginUserDataPath(); } }

        private PanelSettings Settings
        {
            get { if (settings == null) settings = PanelSettings.Load(DataFolder); return settings; }
        }

        /// <summary>The links, read from disk the first time they're needed.</summary>
        private LinkStore Store
        {
            get { if (store == null) store = new LinkStore(DataFolder); return store; }
        }

        private Opener TheOpener
        {
            get { if (opener == null) opener = new Opener(api, seam, handoff, Store, Settings); return opener; }
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                if (api.ApplicationInfo.Mode != ApplicationMode.Fullscreen) return;
                topBar = new TopBarButton(seam, Settings, ButtonPressed);
                topBar.Start();
                // the button's picture, once Playnite has finished starting (the list is read then, not during start-up)
                var app = Application.Current;
                if (app != null) app.Dispatcher.BeginInvoke(new Action(RefreshIcon), DispatcherPriority.ContextIdle);
            }
            catch (Exception ex) { SafeLog.Warn("start failed", ex); }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            try { handoff.Stop(); } catch (Exception ex) { SafeLog.Warn("stop: handoff", ex); }
            try { if (topBar != null) topBar.Stop(); } catch (Exception ex) { SafeLog.Warn("stop: top bar", ex); }
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            return new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Description = "Links",
                    Action = a =>
                    {
                        // (after Playnite's menu has closed and given the activation back, so it doesn't close the panel)
                        try
                        {
                            var app = Application.Current;
                            if (app == null) return;
                            app.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { OpenPanel(null); }
                                catch (Exception ex) { SafeLog.Warn("menu: open failed", ex); }
                            }), DispatcherPriority.ContextIdle);
                        }
                        catch (Exception ex) { SafeLog.Warn("menu: open failed", ex); }
                    }
                }
            };
        }

        // ---------------- the panel ----------------

        /// <summary>
        /// The top bar button's picture: the latest link's site (Kippix's own icon for it, or a letter badge), so the button
        /// looks like what it opens; the plain link icon when that is switched off or there is no link. Only the site is
        /// ever shown there, in Playnite's main window, never the page.
        /// </summary>
        private void RefreshIcon()
        {
            try
            {
                if (topBar == null) return;
                System.Windows.Media.ImageSource pic = null;
                if (Settings.SiteIcon)
                {
                    var items = Store.Items;
                    if (items.Count > 0)
                        pic = SiteIcon.For(items[0].Url, System.IO.Path.Combine(api.Paths.ConfigurationPath, "Themes", "Fullscreen", "Kippix_Kipping0"));
                }
                topBar.SetIcon(pic);
            }
            catch (Exception ex) { SafeLog.Warn("icon: failed", ex); }
        }

        /// <summary>
        /// The top bar button. Like one of Kippix's own streaming shortcuts, it opens a page straight away: the latest link
        /// (the one added or opened last), after a moment's "Opening" card from which the full list can be had instead.
        /// The list comes first when there is nothing to open yet, when a new address has just been copied (ready to add),
        /// or when "Top bar button" is set to show the list.
        /// </summary>
        private void ButtonPressed()
        {
            try
            {
                LinkItem latest = null;
                if (Settings.Direct && !panelOpen)
                {
                    var items = Store.Items;
                    if (items.Count > 0 && !LinksPanel.ClipboardHoldsNew(Store)) latest = items[0];
                }
                OpenPanel(latest, latest != null);
            }
            catch (Exception ex) { SafeLog.Warn("button: failed", ex); }
        }

        /// <summary>Shows the panel (one at a time). focus: the link to highlight first, or null.</summary>
        private void OpenPanel(LinkItem focus) { OpenPanel(focus, false); }

        /// <summary>quick: the "Opening" card for focus instead of the list.</summary>
        private void OpenPanel(LinkItem focus, bool quick)
        {
            var app = Application.Current;
            if (app == null) return;
            if (!app.Dispatcher.CheckAccess()) { app.Dispatcher.BeginInvoke(new Action(() => { try { OpenPanel(focus, quick); } catch (Exception ex) { SafeLog.Warn("panel: open failed", ex); } })); return; }
            if (panelOpen)
            {
                // already up (one at a time): bring it back to the front instead of doing nothing
                if (openPanel != null) openPanel.Bring();
                return;
            }
            panelOpen = true;
            try
            {
                var main = app.MainWindow;
                // where the highlight was in the main window, to put it back afterwards
                IInputElement saved = null;
                try
                {
                    var kf = Keyboard.FocusedElement as DependencyObject;
                    if (kf != null && main != null && ReferenceEquals(Window.GetWindow(kf), main) && !(kf is Window)) saved = (IInputElement)kf;
                }
                catch (Exception) { }
                var savedRef = saved != null ? new WeakReference(saved) : null;

                var panel = new LinksPanel(Store, seam, main, focus != null && Store.Contains(focus) ? focus : null, Settings, quick);
                openPanel = panel;
                panel.Closed += (s, e) =>
                {
                    try
                    {
                        panelOpen = false;
                        openPanel = null;
                        var result = panel.Result;
                        var item = panel.ResultItem;
                        // (closed because something else was activated: don't pull Playnite back over it)
                        BackToMain(main, savedRef, !panel.ClosedByDeactivation);
                        // what was chosen happens once the focus is back in the main window
                        app.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { AfterPanel(result, item); }
                            catch (Exception ex) { SafeLog.Warn("panel: after close", ex); }
                            RefreshIcon();   // (the latest link, or the icon setting, may have changed)
                        }), DispatcherPriority.ContextIdle);
                    }
                    catch (Exception ex) { SafeLog.Warn("panel: closed", ex); }
                };
                panel.Show();
                if (quick) SafeLog.Info("opening card shown"); else SafeLog.Info("panel opened");
            }
            catch (Exception ex)
            {
                panelOpen = false;
                openPanel = null;
                SafeLog.Warn("panel: open failed", ex);
            }
        }

        private static void BackToMain(Window main, WeakReference savedRef, bool activate)
        {
            if (main == null) return;
            Action back = () =>
            {
                try
                {
                    if (activate && main.IsVisible && main.WindowState != WindowState.Minimized && !main.IsActive) main.Activate();
                    var el = savedRef != null ? savedRef.Target as UIElement : null;
                    if (el != null && el.IsVisible && el.Focusable && ReferenceEquals(Window.GetWindow(el), main) && !ReferenceEquals(Keyboard.FocusedElement, el))
                    {
                        if (main.IsActive) Keyboard.Focus(el);
                        else FocusManager.SetFocusedElement(main, el);   // (it gets the focus when Playnite is active again)
                    }
                }
                catch (Exception ex) { SafeLog.Warn("panel: focus back", ex); }
            };
            main.Dispatcher.BeginInvoke(back, DispatcherPriority.Input);
        }

        /// <summary>
        /// What was chosen in the panel. (Rename happens inside the panel, with its own keyboard: Kippix's keyboard is drawn
        /// in the main window, which can be captured, and keeps its text after closing, so no name or title is sent there.)
        /// </summary>
        private void AfterPanel(PanelResult result, LinkItem item)
        {
            if (item == null || !Store.Contains(item)) return;
            if (result == PanelResult.Open) TheOpener.Open(item);
            else if (result == PanelResult.List) OpenPanel(item, false);   // (asked for from the "Opening" card)
        }
    }
}
