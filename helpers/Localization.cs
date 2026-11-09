using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

namespace GitConfigTool.Helpers
{
    public class LanguageInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public static class Localization
    {
        private static readonly List<LanguageInfo> _languages = new List<LanguageInfo>();
        private static Dictionary<string, string> _current = new Dictionary<string, string>();
        private static ResourceDictionary _activeDictionary;

        public static string DefaultLanguageId { get; private set; } = "zh_cn";
        public static string AppliedLanguageId { get; private set; } = "zh_cn";
        public static IReadOnlyList<LanguageInfo> Languages => _languages;

        static Localization()
        {
            var index = ParseFlatObject(ReadEmbedded("index.json"));

            string def;
            if (index.TryGetValue("default", out def) && !string.IsNullOrWhiteSpace(def))
                DefaultLanguageId = def.Trim();

            string order;
            if (index.TryGetValue("order", out order))
            {
                foreach (var raw in order.Split(','))
                {
                    var id = raw.Trim();
                    string name;
                    if (id.Length > 0 && index.TryGetValue(id, out name))
                        _languages.Add(new LanguageInfo { Id = id, Name = name });
                }
            }

            AppliedLanguageId = DefaultLanguageId;
        }

        public static void Apply(string id)
        {
            var json = ReadEmbedded(id + ".json");
            var strings = json == null
                ? new Dictionary<string, string>()
                : ParseFlatObject(json);

            _current = strings;
            AppliedLanguageId = id;

            var app = Application.Current;
            if (app == null) return;

            var next = new ResourceDictionary();
            foreach (var pair in strings)
                next[pair.Key] = pair.Value;

            // 先挂新字典再摘旧字典，切换过程中不会出现文字空白
            app.Resources.MergedDictionaries.Add(next);
            if (_activeDictionary != null)
                app.Resources.MergedDictionaries.Remove(_activeDictionary);
            _activeDictionary = next;
        }

        public static string T(string id)
        {
            string value;
            return _current.TryGetValue(id, out value) ? value : id;
        }

        public static string T(string id, params object[] args)
        {
            return string.Format(T(id), args);
        }

        private static string ReadEmbedded(string fileName)
        {
            var suffix = ".langs." + fileName;
            var assembly = typeof(Localization).Assembly;

            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                using (var stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null) continue;
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        return reader.ReadToEnd();
                }
            }
            return null;
        }

        // 语言包是扁平的 { "id": "text" }，手写解析避免引入 JSON 依赖
        private static Dictionary<string, string> ParseFlatObject(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            while (i < json.Length && json[i] != '{') i++;
            if (i >= json.Length) return result;
            i++;

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"') { i++; continue; }

                var key = ReadString(json, ref i);
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != '"') break;
                result[key] = ReadString(json, ref i);
            }
            return result;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string ReadString(string s, ref int i)
        {
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;

                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        int code;
                        if (i + 4 <= s.Length &&
                            int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
    }
}
