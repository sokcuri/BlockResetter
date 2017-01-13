using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace BlockResetter
{
    internal class Program
    {
        public static string ini_file = "BlockResetter.ini";
        public static IniSettings settings;

        [STAThread]
        private static void Main()
        {
            Console.InputEncoding = Encoding.Unicode;
            Console.OutputEncoding = Encoding.Unicode;

            settings = new IniSettings(new FileInfo(ini_file));

            Console.WriteLine("Loading login info...");
            TwitterApi twitter = TwitterApi.Login(settings);
            if (twitter.OAuth?.User.Token == null) return;
            
            HashSet<string> blocklist = new HashSet<string>();

            string readLine;
            if (!string.IsNullOrEmpty(twitter.MyUserInfo?.id_str))
            {
                Console.WriteLine("Get My Block List... (Max 250000 per 15min)");
                string cursor = "-1";

                while (true)
                    try
                    {
                        UserIdsObject result = JsonConvert.DeserializeObject<UserIdsObject>(twitter.getMyBlockList(cursor));
                        while (result != null)
                        {
                            blocklist.UnionWith(result.ids);
                            if (result.next_cursor == 0)
                                break;
                            result =
                                JsonConvert.DeserializeObject<UserIdsObject>(twitter.getMyBlockList(cursor = result.next_cursor_str));
                        }

                        break;
                    }
                    catch (RateLimitException)
                    {
                        if (Convert.ToBoolean(settings.GetValue("Configuration", "AutoRetry_GetBlockList", false)) == false)
                        {
                            Console.Write("Do you want retry get block list after 15min? (Yes/No/Auto)");
                            readLine = Console.ReadLine();
                            if (readLine != null)
                            {
                                if (readLine.ToUpper().Trim().StartsWith("N"))
                                    break;
                                if (readLine.ToUpper().Trim().StartsWith("A"))
                                    settings.SetValue("Configuration", "AutoRetry_GetBlockList", true);
                            }

                            settings.Save();
                        }

                        Console.WriteLine("Wait for 15min... The job will be resumed at : " +
                                            DateTime.Now.AddMinutes(15).ToString("hh:mm:ss"));
                        Thread.Sleep(TimeSpan.FromMinutes(15));
                    }
            }
            else
            {
                Console.WriteLine("Failed to get your info!");
                Console.ReadKey(true);
                return;
            }

            Console.WriteLine($"Blocklist = {blocklist.Count}");

            long count = 0;
            bool userStopped = false;
            RateLimitException rateLimit = null;
            foreach (string ids in blocklist)
            {
                count++;
                Console.WriteLine(
                    $"Target= {(ids.Length < 18 ? ids : ids.Substring(0, 17) + "...")}, " +
                    $"Progress= {count}/{blocklist.Count} ({Math.Round(count * 100 / (double)blocklist.Count, 2)}%)");

                twitter.UnBlock(ids);

                if (!Console.KeyAvailable) continue;
                while (Console.KeyAvailable)
                    Console.ReadKey(true);
                Console.WriteLine("Do you want stop reset blocklist?");
                if (DialogResult.Yes != MessageBox.Show("Do you want stop reset blocklist?",
                        "Stop ?",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question)) continue;
                rateLimit = null;
                userStopped = true;
                break;
            }

            //Console.Write("Do you want export your block list? (Y/N) : ");
            //readLine = Console.ReadLine();
            //if ((readLine != null) && readLine.ToUpper().Trim().Equals("Y"))
            //    File.WriteAllText($"blocklist_{DateTime.Now:yyyy-MM-dd_HHmm}.csv", string.Join(",", blocklist));


            Console.Write("Finished !");
            settings.SetValue("Authenticate", "AccessToken", "");
            settings.SetValue("Authenticate", "AccessSecret", "");
            settings.Save();
            readLine = Console.ReadLine();
        }

        private static void GetTargetSearchResult(TwitterApi twitter, string target, bool isNewReq, HashSet<string> targetLists)
        {
            Console.WriteLine($"Search {target}...");
            string json = twitter.searchPhase(Uri.EscapeDataString(target), isNewReq);
            if (!string.IsNullOrWhiteSpace(json))
            {
                SearchResultObject result = JsonConvert.DeserializeObject<SearchResultObject>(json);
                while (result.search_metadata.count > 0)
                {
                    targetLists.UnionWith(result.statuses.Select(x => x.user.id_str));
                    result =
                        JsonConvert.DeserializeObject<SearchResultObject>(
                            twitter.searchPhase(result.search_metadata.next_results, false));
                    if (result?.search_metadata.next_results == null) break;
                }
            }
            else
            {
                Console.WriteLine("There is no result.");
            }
        }

        private static void GetTargetFollowers(TwitterApi twitter, string username, string cursor, HashSet<string> targetLists)
        {
            Console.WriteLine($"Get {username}'s Followers...");
            string json = twitter.getFollowers(username, cursor);
            if (!string.IsNullOrWhiteSpace(json))
            {
                UserIdsObject result = JsonConvert.DeserializeObject<UserIdsObject>(json);
                while (result != null)
                {
                    targetLists.UnionWith(result.ids);
                    if (result.next_cursor == 0)
                        break;
                    result =
                        JsonConvert.DeserializeObject<UserIdsObject>(twitter.getFollowers(username,
                            result.next_cursor_str));
                }
            }
            else
            {
                Console.WriteLine("Unable to get target followers.");
            }
        }
    }
}