using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Starrfind
{
    public static class Tests
    {
        static int total;
        static void Assert(bool success, string label)
        {
            if (!success)
                throw new Exception("FAIL: " + label);
            total++;
        }

        public static int Run(string[] args)
        {
            var log = new List<string>();
            string root = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                Assert(Util.Tag(" o28py ") == "#028PY", "Tag normalization");
                Assert(!Util.ValidTag("#ABC123"), "Invalid tag rejected");
                Assert(Util.Match("Ålex", "alex"), "Unicode normalization");
                Assert(Util.Match("#2PPQ", "#2P*"), "Tag wildcard");
                Assert(Util.Match("Alexx", "Alex", true), "Fuzzy one edit");
                Assert(Util.ReadTags("tag,name\r\n\"#2PP\",\"PYP\"\r\n\"#2PPQ\",\"Alex\"", true).Count == 2, "CSV imports tag column only");
                Assert(Util.ReadTags("#2PP\nnot a tag\n#2PP\n#2PPQ", false).Count == 2, "TXT validation and deduplication");
                var html = File.ReadAllText(Path.Combine(root, "fixtures", "ninja_profile.html"));
                var r = Sources.ParseNinja(html, "https://brawltime.ninja/profile/2PPQ");
                Assert(r.Player.Tag == "#2PPQ", "Ninja profile identity");
                Assert(r.Player.Brawlers.Count == 3, "Brawlers extracted");
                Assert(r.Battles.Count > 0, "Battle archive extracted");
                Assert(r.Battles.All(b => b.Time.Kind == DateTimeKind.Utc), "UTC preserved");
                Assert(r.Battles.Any(b => b.Players.Count >= 6), "Participants extracted");
                Assert(r.Battles.Any(b => !b.Change.HasValue), "Missing trophy change remains unknown");
                var list = Sources.ParseFind(File.ReadAllText(Path.Combine(root, "fixtures", "search.html")), "https://www.brawlfind.com/search/player/Alex/0-200000");
                Assert(list.Count == 6, "BrawlFind cards parsed");
                Assert(list[0].Name == "Sample One" && list[0].Trophies == 12345, "Name and exact trophies parsed");
                if (File.Exists(Path.Combine(root, "fixtures", "find-profile.html")))
                {
                    var find = Sources.ParseFindProfile(File.ReadAllText(Path.Combine(root, "fixtures", "find-profile.html")), "https://www.brawlfind.com/player/2PPQ");
                    Assert(find.Player.Tag == "#2PPQ" && find.Player.Trophies == 12345, "BrawlFind profile values");
                    Assert(find.Player.Brawlers.Count == 3, "BrawlFind brawlers");
                    Assert(find.History.Count == 3 && find.History.All(s => s.Source.StartsWith("BrawlFind")), "BrawlFind historical trophies with provenance");
                    Assert(!find.Player.HighestTrophies.HasValue, "Unpublished peak remains unknown");
                }

                var official = JObject.Parse("{\"tag\":\"#2PP\",\"name\":\"Tester\",\"trophies\":42,\"brawlers\":[{\"id\":16000000,\"name\":\"SHELLY\",\"power\":1,\"trophies\":2,\"gadgets\":[]}]}");
                Assert(Sources.ParsePlayer(official, "Supercell API", "").Brawlers.Count == 1, "Official array schema");
                var battles = JArray.Parse("[{\"battleTime\":\"20201215T210000.000Z\",\"event\":{\"mode\":\"brawlBall\",\"map\":\"Test\"},\"battle\":{\"result\":\"victory\",\"trophyChange\":8,\"teams\":[[{\"tag\":\"#2PP\",\"name\":\"Tester\",\"brawler\":{\"name\":\"SHELLY\",\"power\":1}}]]}}]");
                var parsed = Sources.ParseBattles(battles, "#2PP", "Supercell API", false);
                Assert(parsed.Count == 1 && parsed[0].Time.Year == 2020 && parsed[0].Change == 8, "Official battle parsed");
                string temp = Path.Combine(Path.GetTempPath(), "ST4RR-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                using (var db = new Store(Path.Combine(temp, "test.db")))
                {
                    db.Save(r.Player);
                    int added = db.AddBattles(r.Battles);
                    Assert(added > 0, "Battles persisted");
                    Assert(db.AddBattles(r.Battles) == 0, "Battle deduplication");
                    Assert(db.Players.Count() > 1, "Participants indexed");
                    var p = db.Players.FindById(r.Player.Tag);
                    p.Year = 2020;
                    p.YearSource = "test evidence";
                    p.Favorite = true;
                    p.Notes = "retain";
                    db.Players.Update(p);
                    var incoming = Sources.ParseNinja(html, "").Player;
                    db.Save(incoming);
                    var updated = db.Players.FindById(p.Tag);
                    Assert(updated.Year == 2020 && updated.Favorite && updated.Notes == "retain", "User evidence survives refresh");
                    db.Save(new Player { Tag = p.Tag, Name = "index", Source = "BrawlFind", Trophies = 1 });
                    Assert(db.Players.FindById(p.Tag).Detailed && db.Players.FindById(p.Tag).Trophies > 1, "Index cannot overwrite full profile");
                    db.Save(Sources.ParsePlayer(official, "Supercell API", ""));
                    db.AddBattles(parsed);
                    var filter = new SearchFilter
                    {
                        Year = 2020
                    };
                    Assert(filter.Apply(db.Players.FindAll(), db.Battles.FindAll()).Count() == 1, "Year exact excludes unknown");
                    filter.IncludeUnknown = true;
                    Assert(filter.Apply(db.Players.FindAll(), db.Battles.FindAll()).Count() > 1, "Include unknown year");
                    filter = new SearchFilter
                    {
                        From = new DateTime(2020, 12, 13, 0, 0, 0, DateTimeKind.Utc),
                        Until = new DateTime(2020, 12, 18, 0, 0, 0, DateTimeKind.Utc)
                    };
                    Assert(filter.Apply(db.Players.FindAll(), db.Battles.FindAll()).Any(x => x.Tag == "#2PP"), "Date range includes Dec 13-17 battle");
                    filter.Until = new DateTime(2020, 12, 15, 0, 0, 0, DateTimeKind.Utc);
                    Assert(!filter.Apply(db.Players.FindAll(), db.Battles.FindAll()).Any(), "Exclusive upper bound");
                    var export = db.Export();
                    Assert(!Newtonsoft.Json.JsonConvert.SerializeObject(export).Contains("EncryptedKey"), "Backup contains no key");
                    using (var other = new Store(Path.Combine(temp, "other.db")))
                    {
                        other.Import(export);
                        Assert(other.Battles.Count() == db.Battles.Count(), "Backup round trip");
                        other.Import(export);
                        Assert(other.Battles.Count() == db.Battles.Count(), "Idempotent import");
                        Assert(export.Application == "ST4RR", "New backup identity");
                        export.Application = "ST4RRF1ND";
                        other.Import(export);
                        Assert(other.Battles.Count() == db.Battles.Count(), "Legacy backup compatibility");
                    }
                }

                var options = new Options();
                options.SetKey("test-key-not-real");
                Assert(options.Key == "test-key-not-real" && !options.EncryptedKey.Contains("test-key"), "DPAPI protection");
                options.SetKey("");
                Assert(options.Key == "", "Clear key");
                using (var disabled = new Sources(new Options { FindEnabled = false, NinjaEnabled = false, OfficialEnabled = false }))
                {
                    bool rejected = false;
                    try
                    {
                        disabled.Profile("#2PP", CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException)
                    {
                        rejected = true;
                    }

                    Assert(rejected, "Disabled sources are not contacted");
                }

                if (args.Contains("--network"))
                {
                    using (var sources = new Sources(new Options()))
                    {
                        var live = sources.Profile("#8LQ9JR82", CancellationToken.None).GetAwaiter().GetResult();
                        Assert(live.Player.Tag == "#8LQ9JR82" && live.Player.Detailed, "Live keyless profile");
                        log.Add("Live profile: " + live.Player.Name + "; brawlers=" + live.Player.Brawlers.Count + "; battles=" + live.Battles.Count);
                        var found = sources.Search("Alex", 1, CancellationToken.None).GetAwaiter().GetResult();
                        Assert(found.Count > 0, "Live keyless name search");
                        log.Add("Live BrawlFind search: " + found.Count);
                    }
                }

                if (args.Contains("--extended"))
                {
                    using (var sources = new Sources(new Options()))
                    {
                        var alternative = sources.Profile("#8LQ9JR82", CancellationToken.None, "find").GetAwaiter().GetResult();
                        Assert(alternative.Player.Source == "BrawlFind" && alternative.History.Count > 0, "Explicit BrawlFind provider with history");
                        log.Add("Selected provider: BrawlFind; history=" + alternative.History.Count);
                        try
                        {
                            var ranks = sources.Ranking("global", CancellationToken.None).GetAwaiter().GetResult();
                            Assert(ranks.Count > 0, "Live rankings");
                            log.Add("Ranking: " + ranks.Count);
                        }
                        catch (Exception e)
                        {
                            throw new Exception("Ranking failed", e);
                        }

                        try
                        {
                            var events = sources.Events(CancellationToken.None).GetAwaiter().GetResult();
                            Assert(events.HasValues, "Live events");
                            log.Add("Events: " + events.ToString().Substring(0, Math.Min(300, events.ToString().Length)));
                            File.WriteAllText(Path.Combine(root, "events-live.json"), events.ToString());
                        }
                        catch (Exception e)
                        {
                            throw new Exception("Events failed", e);
                        }

                        try
                        {
                            var club = sources.Club("#208UU822P", CancellationToken.None).GetAwaiter().GetResult();
                            Assert(club["members"] != null && club["members"].Any(), "Live club roster");
                            log.Add("Club: " + club["name"] + " members=" + club["members"]?.Count());
                            File.WriteAllText(Path.Combine(root, "club-live.json"), club.ToString());
                        }
                        catch (Exception e)
                        {
                            throw new Exception("Club failed", e);
                        }

                        try
                        {
                            var profileHtml = sources.Http.Get("https://www.brawlfind.com/player/8LQ9JR82", CancellationToken.None).GetAwaiter().GetResult();
                            File.WriteAllText(Path.Combine(root, "find-profile-live.html"), profileHtml);
                            Assert(profileHtml.Length > 1000, "Live BrawlFind profile page");
                            log.Add("BrawlFind profile: " + profileHtml.Length);
                        }
                        catch (Exception e)
                        {
                            throw new Exception("BrawlFind profile failed", e);
                        }
                    }
                }

                log.Add("PASS: " + total + " assertions");
                File.WriteAllLines(Path.Combine(root, "test-results.txt"), log);
                return 0;
            }
            catch (Exception e)
            {
                log.Add(e.ToString());
                File.WriteAllLines(Path.Combine(root, "test-results.txt"), log);
                return 1;
            }
        }
    }
}
