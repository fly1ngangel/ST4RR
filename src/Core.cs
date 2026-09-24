using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LiteDB;
using HtmlAgilityPack;

namespace Starrfind
{
    public class Player
    {
        [BsonId]
        public string Tag { get; set; }
        public string Name { get; set; } = "Без имени";
        public int? Trophies { get; set; }
        public int? HighestTrophies { get; set; }
        public int? Level { get; set; }
        public int? Wins3 { get; set; }
        public int? WinsSolo { get; set; }
        public int? WinsDuo { get; set; }
        public string ClubTag { get; set; } = "";
        public string ClubName { get; set; } = "";
        public int? Year { get; set; }
        public string YearSource { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool Favorite { get; set; }
        public bool Watch { get; set; }
        public bool Detailed { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime FetchedAt { get; set; }
        public string Source { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public string Raw { get; set; } = "{}";
        public List<string> Aliases { get; set; } = new List<string>();
        public List<Fighter> Brawlers { get; set; } = new List<Fighter>();
    }

    public class Fighter
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? Power { get; set; }
        public int? Trophies { get; set; }
        public int? HighestTrophies { get; set; }
        public string Gadgets { get; set; } = "";
        public string StarPowers { get; set; } = "";
        public string Gears { get; set; } = "";
        public string Skin { get; set; } = "";
    }

    public class Participant
    {
        public string Tag { get; set; }
        public string Name { get; set; }
        public string Brawler { get; set; }
        public int Team { get; set; }
        public int? Power { get; set; }
        public int? Trophies { get; set; }
    }

    public class Battle
    {
        [BsonId]
        public string Id { get; set; }
        public string Owner { get; set; }
        public DateTime Time { get; set; }
        public string Mode { get; set; } = "";
        public string Map { get; set; } = "";
        public string Result { get; set; } = "unknown";
        public string Type { get; set; } = "";
        public int? Change { get; set; }
        public int? Rank { get; set; }
        public int? Duration { get; set; }
        public string StarPlayer { get; set; } = "";
        public string Source { get; set; } = "";
        public string Raw { get; set; } = "{}";
        public List<Participant> Players { get; set; } = new List<Participant>();
    }

    public class Snapshot
    {
        [BsonId]
        public string Id { get; set; }
        public string Tag { get; set; }
        public DateTime Time { get; set; }
        public int? Trophies { get; set; }
        public string Name { get; set; }
        public string Club { get; set; }
        public string Source { get; set; }
    }

    public class Bundle
    {
        public int Version { get; set; } = 1;
        public string Application { get; set; } = "ST4RR";
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public List<Player> Players { get; set; } = new List<Player>();
        public List<Battle> Battles { get; set; } = new List<Battle>();
        public List<Snapshot> Snapshots { get; set; } = new List<Snapshot>();
    }

    public class Options
    {
        public string Language { get; set; } = "auto";
        public bool NinjaEnabled { get; set; } = true;
        public bool FindEnabled { get; set; } = true;
        public bool OfficialEnabled { get; set; } = true;
        public string PreferredSource { get; set; } = "auto";
        public bool Animations { get; set; } = true;
        public bool Blur { get; set; } = true;
        public bool AutoRefresh { get; set; } = false;
        public int IntervalMinutes { get; set; } = 15;
        public bool OfficialFirst { get; set; } = true;
        public string EncryptedKey { get; set; } = "";

        [JsonIgnore]
        public string Key
        {
            get
            {
                try
                {
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(EncryptedKey), null, DataProtectionScope.CurrentUser));
                }
                catch
                {
                    return "";
                }
            }
        }

        public void SetKey(string value)
        {
            EncryptedKey = string.IsNullOrWhiteSpace(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }

        public static Options Load(string folder)
        {
            try
            {
                return JsonConvert.DeserializeObject<Options>(File.ReadAllText(Path.Combine(folder, "settings.json"))) ?? new Options();
            }
            catch
            {
                return new Options();
            }
        }

        public void Save(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "settings.json"), JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }

    public static class Util
    {
        public static string Tag(string s)
        {
            return "#" + (s ?? "").Trim().TrimStart('#').ToUpperInvariant().Replace('O', '0');
        }

        public static bool ValidTag(string s)
        {
            return Regex.IsMatch(Tag(s), "^#[0289PYLQGRJCUV]{3,15}$");
        }

        public static string Norm(string s)
        {
            return Regex.Replace((s ?? "").Normalize(NormalizationForm.FormKD), @"\p{Mn}", "").ToUpperInvariant();
        }

        public static string Num(int? n)
        {
            return n.HasValue ? n.Value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")) : "—";
        }

        public static string Stamp(DateTime t)
        {
            return t == default(DateTime) ? "—" : DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }

        public static int? Int(JToken t)
        {
            int n;
            return t != null && int.TryParse(t.ToString(), out n) ? n : (int? )null;
        }

        public static string Str(JToken t)
        {
            return t == null || t.Type == JTokenType.Null ? "" : t.ToString();
        }

        public static string Hash(string s)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "");
        }

        public static List<string> ReadTags(string text, bool csv)
        {
            var result = new HashSet<string>();
            if (!csv)
            {
                foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var s = line.Trim();
                    if (ValidTag(s))
                        result.Add(Tag(s));
                }

                return result.ToList();
            }

            using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(text)))
            {
                parser.TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited;
                var first = text.Split('\n').FirstOrDefault() ?? "";
                parser.SetDelimiters(first.Contains("\t") ? "\t" : first.Contains(";") ? ";" : ",");
                parser.HasFieldsEnclosedInQuotes = true;
                int tagColumn = 0;
                bool firstRow = true;
                while (!parser.EndOfData)
                {
                    var cells = parser.ReadFields();
                    if (cells == null)
                        continue;
                    if (firstRow)
                    {
                        firstRow = false;
                        var column = Array.FindIndex(cells, x => string.Equals(x.Trim(), "tag", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Trim(), "тег", StringComparison.OrdinalIgnoreCase));
                        if (column >= 0)
                        {
                            tagColumn = column;
                            continue;
                        }
                    }

                    if (cells.Length > tagColumn && ValidTag(cells[tagColumn]))
                        result.Add(Tag(cells[tagColumn]));
                }
            }

            return result.ToList();
        }

        public static DateTime ParseTime(JToken token)
        {
            if (token != null && token.Type == JTokenType.Date)
                return token.Value<DateTime>().ToUniversalTime();
            string s = Str(token);
            DateTimeOffset dto;
            DateTime dt;
            if (DateTime.TryParseExact(s, "yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
                return dt;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
                return dto.UtcDateTime;
            return default(DateTime);
        }

        public static string Result(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "victory":
                    return "Победа";
                case "defeat":
                    return "Поражение";
                case "draw":
                    return "Ничья";
                default:
                    return "Результат не указан";
            }
        }

        public static string Mode(string s)
        {
            switch (s)
            {
                case "brawlBall":
                    return "Броулбол";
                case "gemGrab":
                    return "Захват кристаллов";
                case "soloShowdown":
                    return "Одиночное столкновение";
                case "duoShowdown":
                    return "Парное столкновение";
                case "knockout":
                    return "Нокаут";
                case "bounty":
                    return "Награда за поимку";
                case "heist":
                    return "Ограбление";
                case "hotZone":
                    return "Горячая зона";
                case "wipeout":
                    return "Зачистка";
                default:
                    return s;
            }
        }

        public static bool Match(string text, string query, bool fuzzy = false)
        {
            var a = Norm(text);
            var b = Norm(query).Trim();
            if (b.Length == 0)
                return true;
            if (b.Contains("*") || b.Contains("?"))
                return Regex.IsMatch(a, "^" + Regex.Escape(b).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            if (a.Contains(b))
                return true;
            if (!fuzzy || b.Length < 4 || Math.Abs(a.Length - b.Length) > 1)
                return false;
            int[, ] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++)
                d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length] <= 1;
        }
    }

    public class SearchFilter
    {
        public string Query = "", Club = "", Brawler = "";
        public int? Min, Max, Year;
        public bool UnknownYear, IncludeUnknown, Favorites, Fuzzy;
        public DateTime? From, Until;
        public string Sort = "Трофеи ↓";
        public IEnumerable<Player> Apply(IEnumerable<Player> players, IEnumerable<Battle> battles)
        {
            HashSet<string> tags = null;
            if (From.HasValue || Until.HasValue)
                tags = new HashSet<string>(battles.Where(b => (!From.HasValue || b.Time >= From.Value) && (!Until.HasValue || b.Time < Until.Value)).SelectMany(b => b.Players.Select(p => p.Tag).Concat(new[] { b.Owner })));
            var q = players.Where(p => (Util.Match(p.Name, Query, Fuzzy) || Util.Match(p.Tag, Query) || p.Aliases.Any(a => Util.Match(a, Query, Fuzzy))) && (!Favorites || p.Favorite) && (!Min.HasValue || (p.Trophies.HasValue && p.Trophies >= Min)) && (!Max.HasValue || (p.Trophies.HasValue && p.Trophies <= Max)) && (!UnknownYear || !p.Year.HasValue) && (!Year.HasValue || p.Year == Year || (IncludeUnknown && !p.Year.HasValue)) && Util.Match(p.ClubName + " " + p.ClubTag, Club) && (string.IsNullOrWhiteSpace(Brawler) || p.Brawlers.Any(b => Util.Match(b.Name, Brawler))) && (tags == null || tags.Contains(p.Tag)));
            switch (Sort)
            {
                case "Имя А–Я":
                    return q.OrderBy(p => p.Name);
                case "Недавно получены":
                    return q.OrderByDescending(p => p.FetchedAt);
                case "Трофеи ↑":
                    return q.OrderBy(p => p.Trophies ?? int.MaxValue);
                default:
                    return q.OrderByDescending(p => p.Trophies ?? -1);
            }
        }
    }

    public class Store : IDisposable
    {
        readonly LiteDatabase db;
        public ILiteCollection<Player> Players { get; }
        public ILiteCollection<Battle> Battles { get; }
        public ILiteCollection<Snapshot> Snapshots { get; }

        public Store(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            db = new LiteDatabase(path);
            db.UtcDate = true;
            Players = db.GetCollection<Player>("players");
            Battles = db.GetCollection<Battle>("battles");
            Snapshots = db.GetCollection<Snapshot>("snapshots");
            Players.EnsureIndex(p => p.Name);
            Players.EnsureIndex(p => p.Year);
            Battles.EnsureIndex(b => b.Owner);
            Battles.EnsureIndex(b => b.Time);
            Snapshots.EnsureIndex(s => s.Tag);
        }

        public Player Save(Player p, bool userFields = false)
        {
            p.Tag = Util.Tag(p.Tag);
            if (!Util.ValidTag(p.Tag))
                throw new InvalidDataException("Некорректный тег");
            var old = Players.FindById(p.Tag);
            var now = DateTime.UtcNow;
            p.Brawlers = p.Brawlers ?? new List<Fighter>();
            p.Aliases = p.Aliases ?? new List<string>();
            if (old != null)
            {
                if (!userFields)
                {
                    p.Year = old.Year;
                    p.YearSource = old.YearSource;
                    p.Notes = old.Notes;
                    p.Favorite = old.Favorite;
                    p.Watch = old.Watch;
                }

                p.FirstSeen = old.FirstSeen;
                p.Aliases = p.Aliases.Concat(old.Aliases ?? new List<string>()).Distinct().ToList();
                if (!string.Equals(old.Name, p.Name) && !p.Aliases.Contains(old.Name))
                    p.Aliases.Add(old.Name);
                if (!p.Detailed && old.Detailed)
                {
                    old.Aliases = p.Aliases;
                    return old;
                }
            }
            else if (p.FirstSeen == default(DateTime))
                p.FirstSeen = now;
            if (p.FetchedAt == default(DateTime))
                p.FetchedAt = now;
            Players.Upsert(p);
            if (p.Detailed && (old == null || old.Trophies != p.Trophies || old.Name != p.Name || old.ClubTag != p.ClubTag || !Snapshots.Exists(s => s.Tag == p.Tag)))
            {
                var snapshot = new Snapshot
                {
                    Tag = p.Tag,
                    Time = p.FetchedAt,
                    Trophies = p.Trophies,
                    Name = p.Name,
                    Club = p.ClubName,
                    Source = p.Source
                };
                snapshot.Id = Util.Hash(p.Tag + snapshot.Time.ToString("O") + p.Trophies);
                Snapshots.Upsert(snapshot);
            }

            return p;
        }

        public int AddBattles(IEnumerable<Battle> items)
        {
            int added = 0;
            foreach (var b in items)
            {
                if (b.Time == default(DateTime) || !Util.ValidTag(b.Owner))
                    continue;
                b.Owner = Util.Tag(b.Owner);
                b.Players = b.Players ?? new List<Participant>();
                foreach (var person in b.Players)
                    person.Tag = Util.Tag(person.Tag);
                b.Id = Util.Hash(b.Owner + "|" + b.Time.ToUniversalTime().ToString("O") + "|" + b.Mode + "|" + string.Join(",", b.Players.Select(p => p.Tag).OrderBy(t => t)));
                var old = Battles.FindById(b.Id);
                if (old == null)
                {
                    Battles.Insert(b);
                    added++;
                }
                else if (b.Source == "Supercell API" && old.Source != "Supercell API")
                    Battles.Update(b);
                foreach (var person in b.Players.Where(p => Util.ValidTag(p.Tag)))
                    if (!Players.Exists(p => p.Tag == person.Tag))
                        Save(new Player { Tag = person.Tag, Name = person.Name, Source = "Участник боя • " + b.Source, FetchedAt = DateTime.UtcNow });
            }

            return added;
        }

        public List<Battle> ForPlayer(string tag)
        {
            return Battles.Find(b => b.Owner == tag).OrderByDescending(b => b.Time).ToList();
        }

        public Bundle Export()
        {
            return new Bundle
            {
                Players = Players.FindAll().ToList(),
                Battles = Battles.FindAll().ToList(),
                Snapshots = Snapshots.FindAll().ToList()
            };
        }

        public void Import(Bundle b)
        {
            if (b == null || (b.Application != "ST4RR" && b.Application != "ST4RRF1ND") || b.Version != 1 || b.Players == null || b.Battles == null || b.Snapshots == null)
                throw new InvalidDataException("Это не архив ST4RR версии 1.");
            if (b.Players.Count > 100000 || b.Battles.Count > 250000)
                throw new InvalidDataException("Слишком большой архив. Разделите его на части.");
            if (b.Players.Any(p => !Util.ValidTag(p.Tag) || p.Year.HasValue && (p.Year < 2017 || p.Year > DateTime.UtcNow.Year)))
                throw new InvalidDataException("В архиве некорректный тег или год.");
            db.BeginTrans();
            try
            {
                foreach (var p in b.Players)
                {
                    var old = Players.FindById(Util.Tag(p.Tag));
                    if (old == null || p.FetchedAt > old.FetchedAt)
                        Save(p, old == null);
                }

                AddBattles(b.Battles);
                foreach (var s in b.Snapshots.Where(s => Util.ValidTag(s.Tag) && s.Time != default(DateTime)))
                {
                    s.Id = Util.Hash(s.Tag + s.Time.ToString("O") + s.Trophies);
                    Snapshots.Upsert(s);
                }

                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }
        }

        public void Dispose()
        {
            db.Dispose();
        }
    }

    public class ProfileResult
    {
        public Player Player;
        public List<Battle> Battles = new List<Battle>();
        public List<Snapshot> History = new List<Snapshot>();
        public string Notice = "";
    }

    public class Net : IDisposable
    {
        readonly HttpClient client;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public Net()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(35),
                MaxResponseContentBufferSize = 12000000
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ST4RR/1.0.0 (Windows desktop statistics viewer)");
        }

        public async Task<string> Get(string url, CancellationToken ct, string key = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidOperationException("Требуется HTTPS.");
            await gate.WaitAsync(ct);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Get, uri))
                    {
                        if (!string.IsNullOrEmpty(key))
                        {
                            if (uri.Host != "api.brawlstars.com")
                                throw new InvalidOperationException("Ключ разрешён только для Supercell.");
                            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                        }

                        using (var res = await client.SendAsync(req, ct))
                        {
                            if ((int)res.StatusCode == 429 && i < 2)
                            {
                                var delay = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(3 * (i + 1));
                                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds)), ct);
                                continue;
                            }

                            if (!res.IsSuccessStatusCode)
                                throw new IOException(uri.Host + ": HTTP " + (int)res.StatusCode + ((int)res.StatusCode == 403 ? ". Источник отклонил запрос; для API проверьте ключ и разрешённый IP." : (int)res.StatusCode == 404 ? ". Запись не найдена." : ". Попробуйте позднее."));
                            if (res.Content.Headers.ContentLength > 12000000)
                                throw new IOException("Слишком большой ответ источника.");
                            return await res.Content.ReadAsStringAsync();
                        }
                    }
                }

                throw new IOException("Источник временно ограничил запросы.");
            }
            finally
            {
                await Task.Delay(500);
                gate.Release();
            }
        }

        public void Dispose()
        {
            client.Dispose();
            gate.Dispose();
        }
    }

    public class Sources : IDisposable
    {
        public readonly Net Http = new Net();
        readonly Options options;
        public Sources(Options o)
        {
            options = o;
        }

        static JObject Json(string s)
        {
            using (var reader = new JsonTextReader(new StringReader(s))
            {
                DateParseHandling = DateParseHandling.None,
                MaxDepth = 100
            }

            )
                return JObject.Load(reader);
        }

        public static JObject NinjaContext(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var node = doc.GetElementbyId("vike_pageContext");
            if (node == null)
                throw new InvalidDataException("Brawl Time Ninja изменил формат страницы или временно недоступен.");
            return Json(node.InnerText);
        }

        public static JObject NinjaState(string html)
        {
            var d = NinjaContext(html);
            var state = d["piniaState"];
            if (state == null)
                throw new InvalidDataException("В публичной странице нет данных профиля.");
            return Json(state.ToString())["json"]?["brawlstars"] as JObject ?? new JObject();
        }

        public static Player ParsePlayer(JObject j, string source, string url)
        {
            var brawlers = j["brawlers"];
            IEnumerable<JToken> fighters = brawlers is JObject ? ((JObject)brawlers).Properties().Select(p => p.Value) : brawlers is JArray ? (IEnumerable<JToken>)brawlers : Enumerable.Empty<JToken>();
            Func<JToken, string> names = t => t is JArray ? string.Join(", ", t.Select(x => Util.Str(x["name"]))) : "";
            var p = new Player
            {
                Tag = Util.Tag(Util.Str(j["tag"])),
                Name = Util.Str(j["name"]),
                Trophies = Util.Int(j["trophies"]),
                HighestTrophies = Util.Int(j["highestTrophies"]),
                Level = Util.Int(j["expLevel"]),
                Wins3 = Util.Int(j["3vs3Victories"]),
                WinsSolo = Util.Int(j["soloVictories"]),
                WinsDuo = Util.Int(j["duoVictories"]),
                ClubTag = Util.Str(j["club"]?["tag"]),
                ClubName = Util.Str(j["club"]?["name"]),
                Source = source,
                SourceUrl = url,
                FetchedAt = DateTime.UtcNow,
                Detailed = true,
                Raw = j.ToString(Formatting.None)
            };
            p.Brawlers = fighters.Select(b => new Fighter { Id = Util.Int(b["id"]) ?? 0, Name = Util.Str(b["name"]), Power = Util.Int(b["power"]), Trophies = Util.Int(b["trophies"]), HighestTrophies = Util.Int(b["highestTrophies"]), Gadgets = names(b["gadgets"]), StarPowers = names(b["starPowers"]), Gears = names(b["gears"]), Skin = Util.Str(b["skin"]?["name"]) }).ToList();
            if (!Util.ValidTag(p.Tag) || string.IsNullOrWhiteSpace(p.Name))
                throw new InvalidDataException("Источник не вернул корректный профиль.");
            return p;
        }

        public static List<Battle> ParseBattles(JArray list, string owner, string source, bool ninja)
        {
            var result = new List<Battle>();
            if (list == null)
                return result;
            foreach (var j in list.OfType<JObject>())
            {
                var x = ninja ? j : j["battle"] as JObject;
                if (x == null)
                    continue;
                var b = new Battle
                {
                    Owner = Util.Tag(owner),
                    Time = Util.ParseTime(j[ninja ? "timestamp" : "battleTime"]),
                    Mode = Util.Str(j["event"]?["mode"]),
                    Map = Util.Str(j["event"]?["map"]),
                    Result = Util.Str(x["result"]).ToLowerInvariant(),
                    Type = ninja ? (Util.Str(x["ranked"]) == "True" ? "ranked" : "") : Util.Str(x["type"]),
                    Change = Util.Int(x["trophyChange"]),
                    Rank = Util.Int(x["rank"]),
                    Duration = Util.Int(x["duration"]),
                    StarPlayer = Util.Str(x["starPlayer"]?["tag"]),
                    Source = source,
                    Raw = j.ToString(Formatting.None)
                };
                int team = 0;
                Action<JToken, int> add = (v, t) =>
                {
                    var fighter = v["brawler"];
                    var person = new Participant
                    {
                        Tag = Util.Tag(Util.Str(v["tag"])),
                        Name = Util.Str(v["name"]),
                        Team = t,
                        Brawler = ninja ? Util.Str(fighter).ToUpperInvariant() : Util.Str(fighter?["name"]),
                        Power = ninja ? null : Util.Int(fighter?["power"]),
                        Trophies = Util.Int(ninja ? v["brawlerTrophies"] : fighter?["trophies"])
                    };
                    if (Util.ValidTag(person.Tag))
                        b.Players.Add(person);
                };
                if (x["teams"] is JArray teams)
                    foreach (var group in teams.OfType<JArray>())
                    {
                        foreach (var v in group)
                            add(v, team);
                        team++;
                    }

                if (x["players"] is JArray singles)
                    foreach (var v in singles)
                        add(v, team++);
                if (b.Time != default(DateTime))
                    result.Add(b);
            }

            return result;
        }

        public static ProfileResult ParseNinja(string html, string url)
        {
            var j = NinjaState(html)["player"] as JObject;
            if (j == null)
                throw new InvalidDataException("Профиль не найден в публичном источнике.");
            return new ProfileResult
            {
                Player = ParsePlayer(j, "Brawl Time Ninja", url),
                Battles = ParseBattles(j["battles"] as JArray, Util.Str(j["tag"]), "Brawl Time Ninja", true)
            };
        }

        public async Task<ProfileResult> Profile(string tag, CancellationToken ct, string preferred = null)
        {
            tag = Util.Tag(tag);
            if (!Util.ValidTag(tag))
                throw new ArgumentException("Тег содержит недопустимые символы.");
            string err = "";
            preferred = preferred ?? options.PreferredSource;
            if (preferred == "find" && options.FindEnabled)
                try
                {
                    return await FindProfile(tag, ct);
                }
                catch (Exception e)when (!(e is OperationCanceledException))
                {
                    err = "BrawlFind: " + e.Message;
                }

            if (options.OfficialEnabled && (preferred == "auto" || preferred == "official") && !string.IsNullOrEmpty(options.Key))
                try
                {
                    var url = "https://api.brawlstars.com/v1/players/" + Uri.EscapeDataString(tag);
                    var p = ParsePlayer(Json(await Http.Get(url, ct, options.Key)), "Supercell API", url);
                    var r = new ProfileResult
                    {
                        Player = p
                    };
                    try
                    {
                        r.Battles = ParseBattles(Json(await Http.Get(url + "/battlelog", ct, options.Key))["items"] as JArray, tag, "Supercell API", false);
                    }
                    catch (Exception e)when (!(e is OperationCanceledException))
                    {
                        r.Notice = "Профиль получен; журнал боёв недоступен: " + e.Message;
                    }

                    return r;
                }
                catch (Exception e)when (!(e is OperationCanceledException))
                {
                    err = "Официальный API недоступен. " + e.Message;
                }

            var ninjaUrl = "https://brawltime.ninja/profile/" + tag.TrimStart('#');
            if (!options.NinjaEnabled)
            {
                if (options.FindEnabled)
                    return await FindProfile(tag, ct);
                throw new InvalidOperationException("Включите Brawl Time Ninja или настройте официальный API в разделе «Источники».");
            }

            try
            {
                var r = ParseNinja(await Http.Get(ninjaUrl, ct), ninjaUrl);
                r.Notice = err;
                return r;
            }
            catch (Exception e)when (!(e is OperationCanceledException))
            {
                if (options.FindEnabled)
                {
                    var fallback = await FindProfile(tag, ct);
                    fallback.Notice = "Brawl Time Ninja unavailable; BrawlFind used.";
                    return fallback;
                }

                throw new IOException("Не удалось получить профиль. " + err + " " + e.Message + " Сохранённые данные остаются доступны.");
            }
        }

        public async Task<ProfileResult> FindProfile(string tag, CancellationToken ct)
        {
            if (!options.FindEnabled)
                throw new InvalidOperationException("BrawlFind отключён в настройках источников. Доступен локальный поиск.");
            var url = "https://www.brawlfind.com/player/" + Util.Tag(tag).TrimStart('#');
            return ParseFindProfile(await Http.Get(url, ct), url);
        }

        public static ProfileResult ParseFindProfile(string html, string url)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var root = doc.DocumentNode;
            Func<string, string> attr = id => WebUtility.HtmlDecode(doc.GetElementbyId(id)?.GetAttributeValue("mydata", "") ?? "");
            var tag = Util.Tag(attr("vPlayerTag"));
            if (!Util.ValidTag(tag))
                throw new InvalidDataException("Источник не вернул корректный профиль.");
            Func<string, int?> stat = label =>
            {
                var node = root.SelectSingleNode("//img[@alt='" + label + "']/following-sibling::span[1]");
                int n;
                return int.TryParse(Regex.Replace(Clean(node), @"\D", ""), out n) ? n : (int? )null;
            };
            var club = root.SelectSingleNode("//a[contains(@href,'/club/')][.//span[contains(@class,'h3')]]");
            var p = new Player
            {
                Tag = tag,
                Name = attr("vPlayerName"),
                Trophies = stat("Trophies"),
                Wins3 = stat("3 vs 3 Victories"),
                WinsSolo = stat("Solo Victories"),
                WinsDuo = stat("Duo Victories"),
                ClubName = Clean(club),
                ClubTag = club == null ? "" : Util.Tag(club.GetAttributeValue("href", "").Split('/').Last()),
                Detailed = true,
                Source = "BrawlFind",
                SourceUrl = url,
                FetchedAt = DateTime.UtcNow
            };
            foreach (var node in root.SelectNodes("//ul[@id='ulPlayerBrawlers']/li[@data-id]") ?? new HtmlNodeCollection(null))
            {
                Func<string, int?> number = key =>
                {
                    int n;
                    return int.TryParse(node.GetAttributeValue(key, ""), out n) ? n : (int? )null;
                };
                Func<string, string> equipment = folder => string.Join(", ", (node.SelectNodes(".//img[contains(@src,'/" + folder + "/')]") ?? new HtmlNodeCollection(null)).Select(n => WebUtility.HtmlDecode(n.GetAttributeValue("title", ""))).Where(s => s.Length > 0));
                p.Brawlers.Add(new Fighter { Id = number("data-id") ?? 0, Name = WebUtility.HtmlDecode(node.GetAttributeValue("data-name", "")), Power = number("data-power"), Trophies = number("data-trophies"), Gadgets = equipment("gadgets"), StarPowers = equipment("star-powers"), Gears = equipment("gears") });
            }

            var result = new ProfileResult
            {
                Player = p
            };
            if (attr("vPlayerTrophies").Length > 0)
                foreach (var point in JArray.Parse(attr("vPlayerTrophies")))
                {
                    var time = Util.ParseTime(point["date"]);
                    if (time != default(DateTime))
                    {
                        var s = new Snapshot
                        {
                            Tag = tag,
                            Time = time,
                            Trophies = Util.Int(point["trophies"]),
                            Name = "",
                            Club = "",
                            Source = "BrawlFind • history"
                        };
                        s.Id = Util.Hash(tag + time.ToString("O") + s.Trophies);
                        result.History.Add(s);
                    }
                }

            var raw = new JObject
            {
                {
                    "tag",
                    p.Tag
                },
                {
                    "name",
                    p.Name
                },
                {
                    "trophies",
                    p.Trophies
                },
                {
                    "3vs3Victories",
                    p.Wins3
                },
                {
                    "soloVictories",
                    p.WinsSolo
                },
                {
                    "duoVictories",
                    p.WinsDuo
                },
                {
                    "club",
                    new JObject
                    {
                        {
                            "tag",
                            p.ClubTag
                        },
                        {
                            "name",
                            p.ClubName
                        }
                    }
                },
                {
                    "brawlers",
                    JArray.FromObject(p.Brawlers)
                },
                {
                    "source",
                    url
                },
                {
                    "representation",
                    "Parsed public HTML; missing fields are unknown"
                }
            };
            p.Raw = raw.ToString(Formatting.None);
            return result;
        }

        static string Clean(HtmlNode n)
        {
            return n == null ? "" : Regex.Replace(WebUtility.HtmlDecode(n.InnerText), @"\s+", " ").Trim();
        }

        public static List<Player> ParseFind(string html, string url)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var list = new List<Player>();
            var titles = doc.DocumentNode.SelectNodes("//h1[contains(@class,'card-title')]");
            if (titles == null)
                return list;
            foreach (var title in titles)
            {
                var link = title.Ancestors("a").FirstOrDefault();
                if (link == null)
                    continue;
                var href = link.GetAttributeValue("href", "");
                var m = Regex.Match(href, @"/player/([0289PYLQGRJCUV]+)");
                if (!m.Success)
                    continue;
                var card = title.Ancestors("div").FirstOrDefault(n => n.GetAttributeValue("class", "").Split(' ').Contains("card"));
                if (card == null)
                    continue;
                var trophy = Clean(card.SelectSingleNode(".//span[contains(@class,'text-trophies')]"));
                int nT;
                var club = card.SelectSingleNode(".//a[contains(@href,'/club/')]");
                var clubTag = club?.GetAttributeValue("href", "").Split('/').Last() ?? "";
                list.Add(new Player { Tag = Util.Tag(m.Groups[1].Value), Name = Clean(title), Trophies = int.TryParse(Regex.Replace(trophy, @"\D", ""), out nT) ? nT : (int? )null, ClubName = Clean(club), ClubTag = clubTag.Length > 0 ? Util.Tag(clubTag) : "", Source = "BrawlFind • индекс", SourceUrl = url, FetchedAt = DateTime.UtcNow });
            }

            return list.GroupBy(p => p.Tag).Select(g => g.First()).ToList();
        }

        public async Task<List<Player>> Search(string query, int page, CancellationToken ct, int min = 0, int max = 200000)
        {
            if (!options.FindEnabled)
                throw new InvalidOperationException("BrawlFind отключён в настройках источников. Доступен локальный поиск.");
            if (string.IsNullOrWhiteSpace(query) || query.Length > 50)
                throw new ArgumentException("Введите имя или начало тега (до 50 символов).");
            // The public website validates trophy bounds: use its supported range, then filter locally.
            min = Math.Max(0, Math.Min(80000, min / 10000 * 10000));
            max = max > 100000 ? 200000 : Math.Max(10000, (max + 9999) / 10000 * 10000);
            if (max < min)
                max = 200000;
            var q = query.Trim().TrimStart('#');
            var url = "https://www.brawlfind.com/search/player/" + Uri.EscapeDataString(q) + "/" + min + "-" + max + (page > 1 ? "/" + page : "");
            return ParseFind(await Http.Get(url, ct), url);
        }

        public async Task<JObject> Club(string tag, CancellationToken ct)
        {
            tag = Util.Tag(tag);
            if (!Util.ValidTag(tag))
                throw new ArgumentException("Введите корректный тег клуба.");
            if (options.OfficialEnabled && !string.IsNullOrEmpty(options.Key))
                return Json(await Http.Get("https://api.brawlstars.com/v1/clubs/" + Uri.EscapeDataString(tag), ct, options.Key));
            if (!options.FindEnabled)
                throw new InvalidOperationException("BrawlFind отключён в настройках источников. Доступен локальный поиск.");
            var url = "https://www.brawlfind.com/club/" + tag.TrimStart('#');
            var html = await Http.Get(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var heading = doc.DocumentNode.SelectSingleNode("//h1");
            var members = new JArray();
            var seen = new HashSet<string>();
            foreach (var link in doc.DocumentNode.SelectNodes("//a[contains(@href,'/player/')]") ?? new HtmlNodeCollection(null))
            {
                var m = Regex.Match(link.GetAttributeValue("href", ""), @"/player/([0289PYLQGRJCUV]+)");
                var name = Clean(link);
                if (!m.Success || name.Length == 0 || !seen.Add(m.Groups[1].Value))
                    continue;
                members.Add(new JObject { { "tag", Util.Tag(m.Groups[1].Value) }, { "name", name } });
            }

            if (members.Count == 0)
                throw new InvalidDataException("Источник не вернул состав клуба. Можно открыть его страницу или подключить официальный API.");
            return new JObject
            {
                {
                    "tag",
                    tag
                },
                {
                    "name",
                    Clean(heading)
                },
                {
                    "members",
                    members
                },
                {
                    "source",
                    "BrawlFind"
                }
            };
        }

        public async Task<List<Player>> Ranking(string region, CancellationToken ct)
        {
            if (options.OfficialEnabled && !string.IsNullOrEmpty(options.Key))
            {
                var j = Json(await Http.Get("https://api.brawlstars.com/v1/rankings/" + Uri.EscapeDataString(region) + "/players?limit=200", ct, options.Key));
                return (j["items"] as JArray ?? new JArray()).OfType<JObject>().Select(x =>
                {
                    var p = ParsePlayer(x, "Supercell API • рейтинг", "https://developer.brawlstars.com");
                    p.Detailed = false;
                    return p;
                }).ToList();
            }

            if (!options.FindEnabled)
                throw new InvalidOperationException("BrawlFind отключён в настройках источников. Доступен локальный поиск.");
            var url = "https://www.brawlfind.com/rankings/" + Uri.EscapeDataString(region.ToLowerInvariant()) + "/players";
            var html = await Http.Get(url, ct);
            return ParseRanking(html, url);
        }

        public static List<Player> ParseRanking(string html, string url)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var result = new List<Player>();
            var seen = new HashSet<string>();
            foreach (var link in doc.DocumentNode.SelectNodes("//a[contains(@href,'/player/')]") ?? new HtmlNodeCollection(null))
            {
                var name = Clean(link);
                var tag = link.GetAttributeValue("href", "").Split('/').Last();
                if (name.Length == 0 || !Util.ValidTag(tag) || !seen.Add(tag))
                    continue;
                var container = link.Ancestors("tr").FirstOrDefault() ?? link.Ancestors("li").FirstOrDefault(n => n.GetAttributeValue("class", "").Split(' ').Contains("list-group-item")) ?? link.ParentNode.ParentNode;
                var trophies = Clean(container.SelectSingleNode(".//span[contains(@class,'text-trophies')]"));
                int value;
                result.Add(new Player { Tag = Util.Tag(tag), Name = name, Trophies = int.TryParse(Regex.Replace(trophies, @"\D", ""), out value) ? value : (int? )null, Source = "BrawlFind • ranking", SourceUrl = url, FetchedAt = DateTime.UtcNow });
            }

            return result;
        }

        public async Task<JToken> Events(CancellationToken ct)
        {
            if (options.OfficialEnabled && !string.IsNullOrEmpty(options.Key))
                return JToken.Parse(await Http.Get("https://api.brawlstars.com/v1/events/rotation", ct, options.Key));
            if (!options.NinjaEnabled)
                throw new InvalidOperationException("Включите Brawl Time Ninja или настройте официальный API в разделе «Источники».");
            var ctx = NinjaContext(await Http.Get("https://brawltime.ninja/", ct));
            foreach (var q in ctx["vueQueryState"]?["queries"] ?? new JArray())
                if (Util.Str(q["queryKey"]).Contains("active-events-starlist") && ((q["state"]?["data"]?["current"] as JArray)?.Count ?? 0) > 0)
                    return q["state"]["data"];
            foreach (var q in ctx["vueQueryState"]?["queries"] ?? new JArray())
                if (Util.Str(q["queryKey"]).Contains("active-events") && q["state"]?["data"] is JArray arr && arr.Count > 0)
                    return arr;
            throw new InvalidDataException("Публичный источник не вернул ротацию событий.");
        }

        public void Dispose()
        {
            Http.Dispose();
        }
    }
}
