using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // Shows runtime errors on screen (with COPY), so problems on a phone can be reported without a USB cable.
    public class ErrorOverlay : MonoBehaviour
    {
        static ErrorOverlay instance;
        readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
        Canvas canvas;
        Text text;
        string full;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (instance != null) return;
            var go = new GameObject("Error Overlay");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ErrorOverlay>();
            Application.logMessageReceivedThreaded += instance.OnLog;
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            // Unity's own harmless errors in the editor aren't worth a banner.
            if (message.Contains("MetaQuestFeature") || message.Contains("OpenXRProjectValidation")) return;
            pending.Enqueue(message + "\n" + FirstLines(stackTrace, 4));
        }

        static string FirstLines(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var lines = s.Split('\n');
            return string.Join("\n", lines, 0, Mathf.Min(n, lines.Length)).Trim();
        }

        void Update()
        {
            if (!pending.TryDequeue(out var latest)) return;
            while (pending.TryDequeue(out var more)) latest = more;   // show the most recent
            Show(latest);
        }

        void Show(string message)
        {
            full = $"[{Application.version} • {SystemInfo.deviceModel} • Android {SystemInfo.operatingSystem}]\n{message}";
            if (canvas == null) Build();
            canvas.gameObject.SetActive(true);
            text.text = "<b>ERROR:</b> " + UIFactory.Shorten(message.Replace("\n", "  "), 220);
        }

        void Build()
        {
            canvas = UIFactory.CreateCanvas("Error Overlay", world: false);
            canvas.sortingOrder = 100;
            canvas.transform.SetParent(transform, false);
            var card = UIFactory.Card(canvas.transform, Theme.Red, Theme.ShadowMd, "Error");
            card.anchorMin = new Vector2(0.5f, 0);
            card.anchorMax = new Vector2(0.5f, 0);
            card.pivot = new Vector2(0.5f, 0);
            card.sizeDelta = new Vector2(1500, 150);
            card.anchoredPosition = new Vector2(0, 170);
            text = UIFactory.Label(card, "", 22, TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(20, 12);
            text.rectTransform.offsetMax = new Vector2(-330, -12);
            UIFactory.FitInside(text, 14);
            var copy = UIFactory.Button(card, "Copy", () => GUIUtility.systemCopyBuffer = full, false, Theme.White, 24);
            UIFactory.Place((RectTransform)copy.transform, new Vector2(1, 0.5f), new Vector2(140, 80), new Vector2(-170, 0));
            var close = UIFactory.Button(card, "×", () => canvas.gameObject.SetActive(false), false, Theme.White, 34);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1, 0.5f), new Vector2(80, 80), new Vector2(-20, 0));
        }

        void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            if (instance == this) instance = null;
        }
    }
}
