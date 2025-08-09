// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2025 Skillu
using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ProximityScanner", "Skillu", "2.3.0")]
    [Description("Proximity scan via /scan with configurable filters, cooldown, optional permission tiers, admin logs, and a test command. Command-only, no UI.")]
    public class ProximityScanner : CovalencePlugin
    {
        private const string PermUse   = "proximityscanner.use";
        private const string PermAdmin = "proximityscanner.admin";

        private readonly Dictionary<string, float> lastUse = new();
        private readonly List<Timer> activeTimers = new();

        private ConfigData cfg;

        #region Config

        private class Tier
        {
            public string Permission = "proximityscanner.tier1";
            public float  ScanRadius = 125f;
            public float  CooldownSeconds = 480f;
            public int    Priority = 1; // highest wins
        }

        private class Filters
        {
            public bool IgnoreTeammates = true;
            public bool IgnoreAdmins    = true;
            public bool IgnoreSafezone  = true;
            public bool IgnoreSleeping  = true;
            public bool IgnoreDead      = true;
            public bool IgnoreNPC       = true;
        }

        private class Logging
        {
            public bool Enabled         = true;
            public bool EchoToConsole   = true;
            public bool IncludePosition = true;
            public bool IncludeCount    = false;
            public string FileName      = "ProximityScanner";
        }

        // Optional config-based message overrides. If provided, they are pushed into Lang (en) on load.
        private class MsgOverrides
        {
            public string Detected        = "⚠️ Players detected nearby!";
            public string Clear           = "✅ Area is clear.";
            public string CooldownStarted = "🔁 Cooldown started. You can scan again in {time}.";
            public string CooldownLeft    = "⏳ Scan is on cooldown. Time left: {time}.";
            public string CooldownEnded   = "✅ Cooldown expired. You can scan again.";
            public string NoPermission    = "You do not have permission to use this.";
            public string AdminOnly       = "This command is for admins only.";
            public string NotFound        = "Target player not found (online).";
            public string TestHeader      = "🔧 Test scan from {name}:";
        }

        private class ConfigData
        {
            public bool  RequireUsePermission = false;
            public float DefaultScanRadius    = 100f;
            public float DefaultCooldownSeconds = 600f;

            public Filters Filter = new();
            public Logging Log    = new();

            public List<Tier> PermissionTiers = new()
            {
                new Tier { Permission = "proximityscanner.tier1", ScanRadius = 125f, CooldownSeconds = 480f, Priority = 1 },
                new Tier { Permission = "proximityscanner.tier2", ScanRadius = 150f, CooldownSeconds = 360f, Priority = 2 },
                new Tier { Permission = "proximityscanner.tier3", ScanRadius = 175f, CooldownSeconds = 240f, Priority = 3 }
            };

            public MsgOverrides Msg = new(); // optional overrides → fed into Lang on load
        }

        protected override void LoadDefaultConfig()
        {
            cfg = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            cfg = Config.ReadObject<ConfigData>() ?? new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(cfg);

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            foreach (var t in cfg.PermissionTiers)
                if (!string.IsNullOrEmpty(t.Permission))
                    permission.RegisterPermission(t.Permission, this);

            LoadDefaultMessages();
            ApplyConfigMsgOverridesToLang();
        }

        private void Unload()
        {
            foreach (var t in activeTimers) t?.Destroy();
            activeTimers.Clear();
            lastUse.Clear();
        }

        #endregion

        #region Lang

        private void LoadDefaultMessages()
        {
            var en = new Dictionary<string, string>
            {
                ["Detected"]        = "⚠️ Players detected nearby!",
                ["Clear"]           = "✅ Area is clear.",
                ["CooldownStarted"] = "🔁 Cooldown started. You can scan again in {time}.",
                ["CooldownLeft"]    = "⏳ Scan is on cooldown. Time left: {time}.",
                ["CooldownEnded"]   = "✅ Cooldown expired. You can scan again.",
                ["NoPermission"]    = "You do not have permission to use this.",
                ["AdminOnly"]       = "This command is for admins only.",
                ["NotFound"]        = "Target player not found (online).",
                ["TestHeader"]      = "🔧 Test scan from {name}:"
            };
            lang.RegisterMessages(en, this, "en");
        }

        private void ApplyConfigMsgOverridesToLang()
        {
            if (cfg?.Msg == null) return;

            var overrides = new Dictionary<string, string>
            {
                ["Detected"]        = cfg.Msg.Detected,
                ["Clear"]           = cfg.Msg.Clear,
                ["CooldownStarted"] = cfg.Msg.CooldownStarted,
                ["CooldownLeft"]    = cfg.Msg.CooldownLeft,
                ["CooldownEnded"]   = cfg.Msg.CooldownEnded,
                ["NoPermission"]    = cfg.Msg.NoPermission,
                ["AdminOnly"]       = cfg.Msg.AdminOnly,
                ["NotFound"]        = cfg.Msg.NotFound,
                ["TestHeader"]      = cfg.Msg.TestHeader
            };
            lang.RegisterMessages(overrides, this, "en");
        }

        private string L(string key, IPlayer player = null) =>
            lang.GetMessage(key, this, player?.Id);

        private string LTime(string key, float seconds, IPlayer player = null)
        {
            var txt = L(key, player);
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return txt.Replace("{time}", $"{m}m {s}s");
        }

        #endregion

        #region Commands

        [Command("scan")]
        private void CmdScan(IPlayer player, string cmd, string[] args)
        {
            if (cfg.RequireUsePermission && !player.HasPermission(PermUse))
            {
                player.Reply(L("NoPermission", player));
                return;
            }

            var bp = player.Object as BasePlayer;
            if (bp == null || !bp.IsConnected) return;

            var (radius, cooldown) = ResolveEffectiveSettings(player);

            if (IsOnCooldown(player.Id, cooldown))
            {
                player.Reply(LTime("CooldownLeft", GetCooldownLeft(player.Id, cooldown), player));
                return;
            }

            int count;
            bool found = ScanFrom(bp.transform.position, bp, radius, out count);

            player.Reply(found ? L("Detected", player) : L("Clear", player));

            lastUse[player.Id] = Time.realtimeSinceStartup;
            player.Reply(LTime("CooldownStarted", cooldown, player));

            if (cfg.Log.Enabled) LogScan(bp, radius, cooldown, found, count, "user");

            var t = timer.Once(cooldown, () =>
            {
                // Send in the player's current language if still connected
                var again = bp?.IPlayer;
                again?.Reply(L("CooldownEnded", again));
            });
            activeTimers.Add(t);
        }

        [Command("scan.for")]
        private void CmdScanFor(IPlayer caller, string cmd, string[] args)
        {
            var callerBp = caller.Object as BasePlayer;
            if (!caller.HasPermission(PermAdmin) && !IsAdmin(callerBp))
            {
                caller.Reply(L("AdminOnly", caller));
                return;
            }

            if (args.Length < 1)
            {
                caller.Reply("Usage: /scan.for <steamIdOrName>");
                return;
            }

            var target = FindOnlineBasePlayer(args[0]);
            if (target == null)
            {
                caller.Reply(L("NotFound", caller));
                return;
            }

            var (radius, _) = ResolveEffectiveSettings(caller);
            int count;
            bool found = ScanFrom(target.transform.position, target, radius, out count);

            caller.Reply(L("TestHeader", caller).Replace("{name}", target.displayName));
            caller.Reply(found ? L("Detected", caller) : L("Clear", caller));

            if (cfg.Log.Enabled) LogScan(callerBp, radius, 0f, found, count, $"test:{target.UserIDString}");
        }

        #endregion

        #region Core

        private (float radius, float cooldown) ResolveEffectiveSettings(IPlayer player)
        {
            float r = cfg.DefaultScanRadius;
            float c = cfg.DefaultCooldownSeconds;

            int best = int.MinValue;
            foreach (var t in cfg.PermissionTiers)
            {
                if (string.IsNullOrEmpty(t.Permission)) continue;
                if (!player.HasPermission(t.Permission)) continue;
                if (t.Priority >= best)
                {
                    best = t.Priority;
                    r = t.ScanRadius;
                    c = t.CooldownSeconds;
                }
            }
            return (r, c);
        }

        private bool ScanFrom(Vector3 origin, BasePlayer context, float radius, out int count)
        {
            count = 0;

            foreach (var other in BasePlayer.activePlayerList)
            {
                if (other == null || !other.IsConnected) continue;
                if (other == context) continue;

                if (cfg.Filter.IgnoreDead && other.IsDead()) continue;
                if (cfg.Filter.IgnoreSleeping && other.IsSleeping()) continue;
                if (cfg.Filter.IgnoreNPC && IsNpc(other)) continue;
                if (cfg.Filter.IgnoreAdmins && IsAdmin(other)) continue;
                if (cfg.Filter.IgnoreTeammates && AreTeammates(context, other)) continue;
                if (cfg.Filter.IgnoreSafezone && other.InSafeZone()) continue;

                if (Vector3.Distance(origin, other.transform.position) <= radius)
                    count++;
            }

            return count > 0;
        }

        private static bool AreTeammates(BasePlayer a, BasePlayer b) =>
            a != null && b != null && a.currentTeam != 0 && a.currentTeam == b.currentTeam;

        private static bool IsAdmin(BasePlayer p)
        {
            if (p == null) return false;
            if (p.IsAdmin) return true;
            var c = p.net?.connection;
            return c != null && c.authLevel >= 1;
        }

        private static bool IsNpc(BasePlayer p) => p != null && p.IsNpc;

        private BasePlayer FindOnlineBasePlayer(string idOrName)
        {
            if (ulong.TryParse(idOrName, out var id))
            {
                foreach (var bp in BasePlayer.activePlayerList)
                    if (bp.userID == id) return bp;
                return null;
            }

            BasePlayer match = null;
            var needle = idOrName.ToLowerInvariant();
            foreach (var bp in BasePlayer.activePlayerList)
            {
                if (bp.displayName != null && bp.displayName.ToLowerInvariant().Contains(needle))
                {
                    match = bp; break;
                }
            }
            return match;
        }

        #endregion

        #region Cooldown

        private bool IsOnCooldown(string userId, float cooldown) =>
            lastUse.TryGetValue(userId, out var last) &&
            Time.realtimeSinceStartup - last < cooldown;

        private float GetCooldownLeft(string userId, float cooldown)
        {
            if (!lastUse.TryGetValue(userId, out var last)) return 0f;
            return Mathf.Max(0f, cooldown - (Time.realtimeSinceStartup - last));
        }

        #endregion

        #region Logging

        private void LogScan(BasePlayer actor, float radius, float cooldown, bool found, int count, string mode)
        {
            var name = actor != null ? actor.displayName : "unknown";
            var sid  = actor != null ? actor.UserIDString : "0";
            var pos  = actor != null ? actor.transform.position : Vector3.zero;

            var parts = new List<string>
            {
                $"mode={mode}",
                $"actor=\"{name}\"",
                $"steamid={sid}",
                $"radius={radius:0.##}",
                $"result={(found ? "detected" : "clear")}"
            };
            if (cooldown > 0f) parts.Add($"cooldown={cooldown:0}s");
            if (cfg.Log.IncludePosition) parts.Add($"pos=({pos.x:0.0},{pos.y:0.0},{pos.z:0.0})");
            if (cfg.Log.IncludeCount) parts.Add($"count={count}");

            var line = string.Join(" | ", parts);

            if (cfg.Log.EchoToConsole) Puts(line);
            if (!string.IsNullOrEmpty(cfg.Log.FileName))
                LogToFile(cfg.Log.FileName, line, this, true);
        }

        #endregion
    }
}