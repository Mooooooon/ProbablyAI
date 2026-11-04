using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyAI.Hands;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Macros
{
    internal sealed class MacroStore
    {
        internal const string FileName = "macros.local.json";
        internal const int MaxEntries = 32;
        private const int MaxFileCharacters = 2400000;
        private readonly Action<string> write;
        private Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        internal bool LoadFailed { get; }
        internal int ChangeVersion { get; private set; }
        internal string Warning => LoadFailed ? T("宏库无法读取，原文件已保留。") : null;

        internal sealed class Entry
        {
            internal string Id { get; }
            internal long Revision { get; }
            internal JObject Approved { get; }
            internal JObject Draft { get; }
            internal string Name => (string)(Approved ?? Draft)["name"];

            internal Entry(string id, long revision, JObject approved, JObject draft)
            {
                Id = id;
                Revision = revision;
                Approved = (JObject)approved?.DeepClone();
                Draft = (JObject)draft?.DeepClone();
            }

            internal Entry Copy() => new Entry(Id, Revision, Approved, Draft);
        }

        internal MacroStore(Func<string> read, Action<string> write)
        {
            this.write = write;
            try
            {
                string json = read();
                if (json == null) return;
                if (json.Length > MaxFileCharacters) throw new FormatException();
                using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read() || root["version"]?.Type != JTokenType.Integer || (string)root["version"] != "1" ||
                    !(root["macros"] is JArray array) || array.Count > MaxEntries) throw new FormatException();
                var loaded = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (var token in array)
                {
                    if (!(token is JObject value) || value["id"]?.Type != JTokenType.String ||
                        !Guid.TryParseExact((string)value["id"], "N", out _) || !TryRevision(value["revision"], out long revision))
                        throw new FormatException();
                    var approved = LoadDefinition(value["approved"]);
                    var draft = LoadDefinition(value["draft"]);
                    if (approved == null && draft == null) throw new FormatException();
                    var id = (string)value["id"];
                    loaded.Add(id, new Entry(id, revision, approved, draft));
                }
                entries = loaded;
            }
            catch (Exception) { LoadFailed = true; }
        }

        private static JObject LoadDefinition(JToken token)
        {
            if (token == null) return null;
            if (!(token is JObject definition)) throw new FormatException();
            MacroDefinition.Validate(definition);
            return definition;
        }

        internal static MacroStore Open(Func<string> getDirectory)
        {
            return new MacroStore(() =>
            {
                string path = Path.Combine(getDirectory(), FileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }, json =>
            {
                string directory = getDirectory();
                string path = Path.Combine(directory, FileName);
                System.IO.Directory.CreateDirectory(directory);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, json);
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            });
        }

        internal IReadOnlyList<Entry> List()
            => entries.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal).Select(entry => entry.Copy()).ToArray();

        internal Entry Get(string id)
        {
            if (id != null && entries.TryGetValue(id, out var entry)) return entry.Copy();
            throw new HandsException("macro_not_found", T("找不到这个宏。"));
        }

        internal Entry Save(string id, long? revision, JObject definition, bool draft)
        {
            Entry current = null;
            if (id == null)
            {
                if (revision.HasValue) throw MacroDefinition.Invalid("revision");
                if (entries.Count >= MaxEntries) throw new HandsException("macro_library_full", T("宏库已满。"));
                id = Guid.NewGuid().ToString("N");
            }
            else current = AtRevision(id, revision);

            var next = new Entry(id, NextRevision(current), draft ? current?.Approved : definition, draft ? definition : null);
            Update(id, next);
            return next.Copy();
        }

        internal void Approve(string id, long revision, Action<JObject> validate)
        {
            var current = AtRevision(id, revision);
            RequireDraft(current);
            validate(current.Draft);
            Update(id, new Entry(id, NextRevision(current), current.Draft, null));
        }

        internal void Discard(string id, long revision)
        {
            var current = AtRevision(id, revision);
            RequireDraft(current);
            Update(id, current.Approved == null ? null : new Entry(id, NextRevision(current), current.Approved, null));
        }

        internal void Delete(string id, long revision)
        {
            AtRevision(id, revision);
            Update(id, null);
        }

        private static void RequireDraft(Entry entry)
        {
            if (entry.Draft == null) throw new HandsException("macro_no_draft", T("此宏没有待审草稿。"));
        }

        private Entry AtRevision(string id, long? revision)
        {
            var current = Get(id);
            if (current.Revision != revision)
                throw new HandsException("macro_revision_conflict", T("宏已有新版本，请重新载入后再编辑。"));
            return current;
        }

        internal static bool TryRevision(JToken token, out long revision)
        {
            revision = 0;
            return token?.Type == JTokenType.Integer && long.TryParse((string)token, out revision) && revision > 0;
        }

        private static long NextRevision(Entry current)
            => checked((current?.Revision ?? 0) + 1);

        private void Update(string id, Entry entry)
        {
            if (LoadFailed) throw new HandsException("macro_store_unavailable", Warning);
            var next = new Dictionary<string, Entry>(entries, StringComparer.Ordinal);
            if (entry == null) next.Remove(id);
            else next[id] = entry;
            var array = new JArray();
            foreach (var value in next.Values)
            {
                var record = new JObject { ["id"] = value.Id, ["revision"] = value.Revision };
                if (value.Approved != null) record["approved"] = value.Approved.DeepClone();
                if (value.Draft != null) record["draft"] = value.Draft.DeepClone();
                array.Add(record);
            }
            try { write(new JObject { ["version"] = 1, ["macros"] = array }.ToString(Formatting.None)); }
            catch (Exception) { throw new HandsException("macro_save_failed", T("宏库保存失败，请检查配置目录是否可写。")); }
            entries = next;
            ChangeVersion++;
        }
    }
}
