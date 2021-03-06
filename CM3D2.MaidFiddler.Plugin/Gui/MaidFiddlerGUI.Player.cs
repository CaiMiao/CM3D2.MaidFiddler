﻿using System;
using System.Collections.Generic;
using CM3D2.MaidFiddler.Hook;
using CM3D2.MaidFiddler.Plugin.Utils;

namespace CM3D2.MaidFiddler.Plugin.Gui
{
    public partial class MaidFiddlerGUI
    {
        private readonly Dictionary<PlayerChangeType, Action> playerValueUpdateQueue;
        public PlayerInfo Player { get; }

        public void ReloadPlayer()
        {
            InvokeAsync((Action) Player.UpdateAll);
        }

        public void UpdatePlayerValues()
        {
            if (playerValueUpdateQueue.Count <= 0)
                return;
            Debugger.Assert(
            () =>
            {
                foreach (KeyValuePair<PlayerChangeType, Action> update in playerValueUpdateQueue)
                {
                    update.Value();
                }
            },
            "Failed to update player values");
            playerValueUpdateQueue.Clear();
        }
    }
}