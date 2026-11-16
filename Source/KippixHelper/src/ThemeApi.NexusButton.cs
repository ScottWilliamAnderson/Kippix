using System;

namespace KippixHelper
{
    /// <summary>
    /// (r15) The game page's Nexus Mods button (right of More): shown when the game has a page on Nexus Mods (the Nexus Mods
    /// checker add-on's link, or the game found in Nexus Mods' list of games, ThemeApi.Mods.cs). Pressing it opens the game's
    /// Nexus Mods page in the theme's browser; with Vortex or Mod Organizer 2 installed it offers a small choice first (on the
    /// More menu's page over the game page: B goes back). Its switch is Settings › Game page › Buttons and extras (the old
    /// ShowNexusLink setting, so what was chosen before stays). While the button shows, the Mods card leaves out its own
    /// Nexus Mods / Vortex / Mod Organizer 2 chips (one way in, not two); with the button off they're back on the card.
    /// </summary>
    public partial class ThemeApi
    {
        /// <summary>The game's page on Nexus Mods: the Mods card's match (the add-on's link or Nexus Mods' list), else the add-on's link or search.</summary>
        private string NexusButtonUrl
        {
            get { return !string.IsNullOrEmpty(detModsUrl) ? detModsUrl : detNexusUrl; }
        }

        /// <summary>The switch (Game page › Buttons and extras).</summary>
        public bool DetNexusButton
        {
            get { return settings.ShowNexusLink; }
            set
            {
                if (settings.ShowNexusLink == value) return;
                settings.ShowNexusLink = value;
                SaveSettings();
                Notify("DetNexusButton");
                // with the Mods card off, the game's Nexus Mods page is only looked up for the button
                if (value && detailsOpen && !settings.DetSecMods) FillGameMods(SelectedGameNow(), detMoreGen);
                NotifyNexusButton();
            }
        }

        /// <summary>The Mods card's own buttons: only while the Nexus Mods button isn't on the game page.</summary>
        public bool ShowModsCardButtons { get { return !ShowDetNexus; } }

        private void NotifyNexusButton()
        {
            Notify("ShowDetNexus");
            Notify("ShowModsCardButtons");
        }

        /// <summary>The button: straight to Nexus Mods, or (a mod manager installed) the choice of Nexus Mods or the mod manager.</summary>
        private void OpenNexusChoice()
        {
            string url = NexusButtonUrl;
            if (string.IsNullOrEmpty(url)) return;
            FindModManagers();
            bool vortex = ShowModsVortex, mo2 = ShowModsMo2;
            if (!vortex && !mo2) { PlaySound("select"); OpenLink(url); return; }
            var g = PanelGame();
            if (g == null) return;
            PlaySound("select");
            GameMoreTitle = Loc("LOC_KX_NexusChoiceTitle", "Nexus Mods");
            Notify("GameMoreTitle");
            BuildNexusChoiceRows(url, vortex, mo2);
            if (openPanel != "options") TogglePanel("options");
            SetOptionsPage("gamemore");
        }

        private void BuildNexusChoiceRows(string url, bool vortex, bool mo2)
        {
            GameMoreRows.Clear();
            if (collectionsShown) { collectionsShown = false; Notify("GameCollectionsShown"); Notify("GameMoreListShown"); }
            string page = url;
            GameMoreRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_ModsOpenNexus", "Open on Nexus Mods"), Sub = Loc("LOC_KX_NexusChoiceWebSub", "The game's page, in the browser"),
                Icon = "globe", Key = "nexusweb",
                Command = new RelayCommand(_ => { ClosePanels(); OpenLink(page); })
            });
            if (vortex)
                GameMoreRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_ModsOpenVortex", "Open Vortex"), Sub = Loc("LOC_KX_NexusChoiceAppSub", "Your mod manager, in its own window"),
                    Icon = "sliders", Key = "vortex",
                    Command = new RelayCommand(_ => { ClosePanels(); StartModManager(vortexExe, "Vortex"); })
                });
            if (mo2)
                GameMoreRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_ModsOpenMo2", "Open Mod Organizer 2"), Sub = Loc("LOC_KX_NexusChoiceAppSub", "Your mod manager, in its own window"),
                    Icon = "sliders", Key = "mo2",
                    Command = new RelayCommand(_ => { ClosePanels(); StartModManager(mo2Exe, "Mod Organizer 2"); })
                });
        }
    }
}
