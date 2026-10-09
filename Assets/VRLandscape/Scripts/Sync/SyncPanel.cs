using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // The SYNC screen, built to need as few decisions as possible:
    //  - Readiness checks first (Wi-Fi on, "Nearby devices" allowed) with one-tap fixes.
    //  - PRESENT: one tap. The phone creates and advertises a local room.
    //  - JOIN: nearby presentations appear by themselves; if there's exactly one, it joins after a 3 s countdown.
    //  - Room code entry stays as a backup.
    public class SyncPanel : MonoBehaviour
    {
        const float AutoJoinSeconds = 3f;

        ViewSync sync;
        bool world;
        Action onClose;
        RectTransform body;
        Text statusText, countdownText;
        bool codeMode, autoJoinCancelled;
        string codeEntry = "";
        Text codeText;
        float nextRefresh, autoJoinAt = -1f;
        string lastSignature;
        ViewSync.Presenter autoJoinTarget;

        public static SyncPanel Create(bool world, Action onClose)
        {
            var canvas = UIFactory.CreateCanvas("Sync Panel", world, new Vector2(1920, 1080));
            canvas.sortingOrder = 25;
            var panel = canvas.gameObject.AddComponent<SyncPanel>();
            panel.sync = ViewSync.Ensure();
            panel.world = world;
            panel.onClose = onClose;

            var bg = UIFactory.Panel(canvas.transform, Theme.Cream, "Background");
            UIFactory.Pattern(bg, Theme.Grid, 40f);

            var card = UIFactory.Card(bg, Theme.White, Theme.ShadowLg, "Card");
            UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(1280, 900), Vector2.zero);
            UIFactory.Tilt(card, -0.5f);
            var tag = UIFactory.Badge(card, "View sync", Theme.Yellow, 40, -3f);
            UIFactory.Place(tag, new Vector2(0, 1), new Vector2(330, 80), new Vector2(-20, 40));
            var close = UIFactory.Button(card, "×", panel.Close, world, Theme.White, 44);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(90, 90), new Vector2(-24, -24));

            panel.body = UIFactory.Rect(card, "Body");
            UIFactory.Stretch(panel.body, 40);
            panel.body.offsetMax = new Vector2(-40, -110);

            LocalGroup.RequestPermissions();
            if (panel.sync.CurrentRole == ViewSync.Role.None) panel.sync.StartBrowsing();
            panel.Rebuild();
            return panel;
        }

        void Close()
        {
            sync.StopBrowsing();
            onClose?.Invoke();
            Destroy(gameObject);
        }

        // ------------------------------------------------------------ live updates

        void Update()
        {
            if (statusText != null) statusText.text = StatusLine();
            UpdateAutoJoin();

            if (sync.CurrentRole == ViewSync.Role.Follower && sync.HasState) { Close(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            if (Signature() != lastSignature) Rebuild();
        }

        // What the screen depends on; it's rebuilt only when this changes.
        string Signature()
        {
            string nearby = sync.CurrentRole == ViewSync.Role.None && !codeMode
                ? string.Join(",", sync.Nearby().Select(p => p.Key + p.park))
                : "";
            return $"{sync.CurrentRole}|{sync.ViewerCount}|{codeMode}|{LocalGroup.WifiOn}|{LocalGroup.HasPermission}|{nearby}";
        }

        void UpdateAutoJoin()
        {
            if (countdownText == null || autoJoinTarget == null || autoJoinAt < 0) return;
            float left = autoJoinAt - Time.unscaledTime;
            if (left > 0)
            {
                countdownText.text = $"Joining {UIFactory.Shorten(autoJoinTarget.name, 20)} in {Mathf.CeilToInt(left)}...";
                return;
            }
            autoJoinAt = -1f;
            countdownText.text = "Joining " + UIFactory.Shorten(autoJoinTarget.name, 20) + "...";
            sync.JoinNearby(autoJoinTarget);
        }

        string StatusLine()
        {
            if (sync.CurrentRole == ViewSync.Role.Presenter)
            {
                string room;
                switch (LocalGroup.State)
                {
                    case "starting": room = "Creating your local room..."; break;
                    case "failed": room = "Local room unavailable (" + LocalGroup.Error + "). Viewers on the same Wi-Fi or your hotspot can still join."; break;
                    default: room = "Viewers just open SYNC on their phone. You'll appear automatically."; break;
                }
                return $"{sync.ViewerCount} viewer(s) watching. {room}";
            }
            if (!string.IsNullOrEmpty(sync.Status) && sync.Status.StartsWith("Joining"))
            {
                string s = LocalGroup.State;
                if (s == "failed") return "Couldn't join: " + LocalGroup.Error;
                if (s == "joined") return "Connected. Finding the presenter...";
                return "Connecting... (accept the prompt if your phone asks)";
            }
            return "Phones connect to each other directly. No internet, data or hotspot needed.";
        }

        // ------------------------------------------------------------ layout

        void Rebuild()
        {
            lastSignature = Signature();
            foreach (Transform c in body) Destroy(c.gameObject);
            statusText = countdownText = codeText = null;

            var layout = body.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 20;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 12, 0, 12);

            statusText = UIFactory.Label(body, StatusLine(), 26, TextAnchor.MiddleLeft);
            UIFactory.FitInside(statusText, 16);
            UIFactory.Layout(statusText, preferredHeight: 70);

            ReadinessChecks();
            if (sync.CurrentRole == ViewSync.Role.Presenter) BuildPresenting();
            else if (sync.CurrentRole == ViewSync.Role.Follower) BuildFollowing();
            else if (codeMode) BuildCodeEntry();
            else BuildChoose();
        }

        // One-tap fixes for the two things that stop phones finding each other.
        void ReadinessChecks()
        {
            if (!LocalGroup.WifiOn)
                Problem("Wi-Fi is off. It just needs to be ON (no network or data needed).", "Turn on Wi-Fi", LocalGroup.OpenWifiSettings);
            if (!LocalGroup.HasPermission)
                Problem("Allow \"Nearby devices\" so phones can find each other.", "Allow", LocalGroup.RequestPermissions);
        }

        void Problem(string text, string button, Action fix)
        {
            var card = UIFactory.Card(body, Theme.Yellow, Theme.ShadowSm, "Problem");
            UIFactory.Layout(card, preferredHeight: 96);
            var h = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(20, 14, 10, 10);
            h.spacing = 16;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            var t = UIFactory.Label(card, text, 26, TextAnchor.MiddleLeft);
            UIFactory.FitInside(t, 16);
            UIFactory.Layout(t, flexibleWidth: 1);
            UIFactory.Layout(UIFactory.Button(card, button, fix, world, Theme.Red, 26), preferredWidth: 260);
        }

        void BuildChoose()
        {
            var row = UIFactory.Rect(body, "Choose");
            UIFactory.Layout(row, preferredHeight: 520);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 30;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;

            // PRESENT: one big tap
            var present = UIFactory.Card(row, Theme.Red, Theme.ShadowMd, "Present");
            UIFactory.Layout(present, preferredWidth: 440);
            var pt = UIFactory.Heading(present, "Present", 64, TextAnchor.UpperLeft);
            UIFactory.Stretch(pt.rectTransform, 26);
            var pd = UIFactory.Label(present, "Everyone nearby can follow what you see.", 28, TextAnchor.UpperLeft);
            pd.rectTransform.anchorMin = new Vector2(0, 0);
            pd.rectTransform.anchorMax = new Vector2(1, 1);
            pd.rectTransform.offsetMin = new Vector2(26, 140);
            pd.rectTransform.offsetMax = new Vector2(-26, -110);
            var go = UIFactory.Button(present, "Start →", () => { sync.StopBrowsing(); sync.StartPresenting(true); Rebuild(); }, world, Theme.Yellow, 36);
            UIFactory.Place((RectTransform)go.transform, new Vector2(0.5f, 0), new Vector2(340, 100), new Vector2(0, 26));

            // JOIN: live list of nearby presentations
            var join = UIFactory.Card(row, Theme.Violet, Theme.ShadowMd, "Join");
            UIFactory.Layout(join, flexibleWidth: 1);
            var jt = UIFactory.Heading(join, "Join", 64, TextAnchor.UpperLeft);
            UIFactory.Stretch(jt.rectTransform, 26);

            var list = UIFactory.Rect(join, "Nearby");
            UIFactory.Stretch(list, 26);
            list.offsetMax = new Vector2(-26, -110);
            list.offsetMin = new Vector2(26, 110);
            var vl = list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 14;
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandHeight = false;

            var nearby = sync.Nearby();
            if (nearby.Count == 0)
            {
                var searching = UIFactory.Label(list, "Looking for presentations nearby...\nAsk the presenter to tap PRESENT.", 28, TextAnchor.MiddleCenter);
                UIFactory.Layout(searching, preferredHeight: 200);
                UIFactory.Star(join, 90, Theme.Yellow, new Vector2(1, 1), new Vector2(-20, -16));
                autoJoinTarget = null;
            }
            foreach (var p in nearby.Take(3))
            {
                var entry = UIFactory.Card(list, Theme.White, Theme.ShadowSm, "Presenter");
                UIFactory.Layout(entry, preferredHeight: 92);
                var h = entry.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(18, 12, 10, 10);
                h.spacing = 14;
                h.childControlWidth = h.childControlHeight = true;
                h.childForceExpandWidth = false;
                var name = UIFactory.Label(entry, $"<b>{UIFactory.Shorten(p.name, 22)}</b>\n{UIFactory.Shorten(p.park, 30)}", 24, TextAnchor.MiddleLeft);
                UIFactory.FitInside(name, 14);
                UIFactory.Layout(name, flexibleWidth: 1);
                var target = p;
                UIFactory.Layout(UIFactory.Button(entry, "Join", () => { autoJoinAt = -1f; sync.JoinNearby(target); }, world, Theme.Red, 26), preferredWidth: 140);
            }

            // Exactly one presentation nearby: join it automatically unless cancelled.
            if (nearby.Count == 1 && !autoJoinCancelled)
            {
                if (autoJoinTarget == null || autoJoinTarget.Key != nearby[0].Key) autoJoinAt = Time.unscaledTime + AutoJoinSeconds;
                autoJoinTarget = nearby[0];
                var bar = UIFactory.Rect(list, "Auto join");
                UIFactory.Layout(bar, preferredHeight: 70);
                var bh = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
                bh.spacing = 14;
                bh.childControlWidth = bh.childControlHeight = true;
                bh.childForceExpandWidth = false;
                countdownText = UIFactory.Heading(bar, "", 26, TextAnchor.MiddleLeft);
                UIFactory.FitInside(countdownText, 14);
                UIFactory.Layout(countdownText, flexibleWidth: 1);
                UIFactory.Layout(UIFactory.Button(bar, "Cancel", () => { autoJoinCancelled = true; autoJoinAt = -1f; Rebuild(); }, world, Theme.White, 22), preferredWidth: 150);
            }
            else if (nearby.Count != 1) autoJoinAt = -1f;

            var codeLink = UIFactory.Button(join, "Use a room code", () => { codeMode = true; autoJoinAt = -1f; Rebuild(); }, world, Theme.White, 22);
            UIFactory.Place((RectTransform)codeLink.transform, new Vector2(0.5f, 0), new Vector2(300, 70), new Vector2(0, 26));
        }

        void BuildPresenting()
        {
            var room = UIFactory.Card(body, Theme.Yellow, Theme.ShadowMd, "Live");
            UIFactory.Layout(room, preferredHeight: 300);
            var live = UIFactory.Heading(room, $"You're live • {sync.ViewerCount} watching", 54, TextAnchor.UpperCenter);
            UIFactory.Stretch(live.rectTransform, 26);
            UIFactory.FitInside(live, 28);
            var hint = UIFactory.Label(room, "Viewers: open SYNC. Your presentation shows up by itself.", 28, TextAnchor.MiddleCenter);
            UIFactory.Stretch(hint.rectTransform, 26);
            if (LocalGroup.Supported)
            {
                var code = UIFactory.Badge(room, "Backup room code " + sync.RoomCode, Theme.White, 26, -1.5f);
                UIFactory.Place(code, new Vector2(0.5f, 0), new Vector2(460, 64), new Vector2(0, 26));
            }
            UIFactory.Layout(UIFactory.Button(body, "Stop presenting", () => { sync.Stop(); sync.StartBrowsing(); Rebuild(); }, world, Theme.Red, 32), preferredHeight: 100);
            UIFactory.Layout(UIFactory.Button(body, "Back to the app", Close, world, Theme.White, 28), preferredHeight: 90);
        }

        void BuildFollowing()
        {
            var info = UIFactory.Card(body, Theme.Violet, Theme.ShadowMd, "Following");
            UIFactory.Layout(info, preferredHeight: 220);
            var t = UIFactory.Heading(info, "Following " + UIFactory.Shorten(sync.PresenterName ?? "", 24), 48, TextAnchor.MiddleCenter);
            UIFactory.Stretch(t.rectTransform, 20);
            UIFactory.FitInside(t, 24);
            UIFactory.Layout(UIFactory.Button(body, "Leave", () => { sync.Stop(); autoJoinCancelled = true; sync.StartBrowsing(); Rebuild(); }, world, Theme.Red, 32), preferredHeight: 100);
            UIFactory.Layout(UIFactory.Button(body, "Back to the app", Close, world, Theme.White, 28), preferredHeight: 90);
        }

        // Backup: type the presenter's 4-digit room code.
        void BuildCodeEntry()
        {
            var card = UIFactory.Card(body, Theme.Violet, Theme.ShadowMd, "Code");
            UIFactory.Layout(card, preferredHeight: 520);
            var title = UIFactory.Heading(card, "Room code", 48, TextAnchor.UpperLeft);
            UIFactory.Stretch(title.rectTransform, 26);
            var box = UIFactory.Card(card, Theme.White, Vector2.zero, "Digits");
            UIFactory.Place(box, new Vector2(0.5f, 1), new Vector2(360, 100), new Vector2(0, -100));
            codeText = UIFactory.Heading(box, codeEntry.PadRight(4, '_'), 64, TextAnchor.MiddleCenter);
            UIFactory.Stretch(codeText.rectTransform);

            var pad = UIFactory.Rect(card, "Keypad");
            UIFactory.Place(pad, new Vector2(0.5f, 0), new Vector2(620, 180), new Vector2(0, 110));
            var grid = pad.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(108, 78);
            grid.spacing = new Vector2(14, 16);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.MiddleCenter;
            foreach (var k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" })
            {
                var key = k;
                UIFactory.Button(pad, key, () => TypeDigit(key), world, Theme.White, 36);
            }
            var del = UIFactory.Button(card, "←", () => { if (codeEntry.Length > 0) codeEntry = codeEntry.Substring(0, codeEntry.Length - 1); ShowCode(); }, world, Theme.White, 36);
            UIFactory.Place((RectTransform)del.transform, new Vector2(1, 1), new Vector2(100, 100), new Vector2(-26, -100));
            var back = UIFactory.Button(card, "← Nearby list", () => { codeMode = false; codeEntry = ""; Rebuild(); }, world, Theme.White, 22);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0.5f, 0), new Vector2(300, 70), new Vector2(0, 22));
        }

        void TypeDigit(string d)
        {
            if (codeEntry.Length >= 4) return;
            codeEntry += d;
            ShowCode();
            if (codeEntry.Length == 4) sync.JoinRoom(codeEntry);
        }

        void ShowCode()
        {
            if (codeText != null) codeText.text = codeEntry.PadRight(4, '_');
        }
    }
}
