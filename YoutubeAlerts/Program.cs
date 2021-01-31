using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenQA.Selenium.Chrome;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace YoutubeAlerts
{
    class Program
    {
        public static MongoCRUD db;
        static ManualResetEvent _quitEvent = new ManualResetEvent(false);

        //Youtuber watchlist, custom URLS only so far
        public static List<string> youtubers = new List<string>() {
                "https://www.youtube.com/c/theWalrusStreet",
                "https://www.youtube.com/c/GriffinMilks",
                "https://www.youtube.com/c/PatrickWieland",
                "https://www.youtube.com/channel/UCNeBCizpA1NbX7A9_ia6jYg",
                "https://www.youtube.com/channel/UCsKdUbXfgsrxtGdpaPoOiFQ",
                "https://www.youtube.com/channel/UCcIvNGMBSQWwo1v3n-ZRBCw",
                "https://www.youtube.com/c/FatherOfFinance",

            };

        public static List<string> bannedWords = new List<string>() {
            "THE", "FUCK", "ING", "CEO", "USD", "WSB", "FDA", "NEWS", "FOR", "YOU", "AMTES", "WILL", "CDT", "SUPPO", "MERGE",
            "BUY", "HIGH", "ADS", "FOMO", "THIS", "OTC", "ELI", "IMO", "TLDR", "SHIT", "ETF", "BOOM", "THANK", "MAYBE", "AKA",
            "CBS", "SEC", "NOW", "OVER", "ROPE", "MOON", "SSR", "HOLD", "SELL", "COVID", "GROUP", "MONDA", "PPP", "REIT", "HOT",
            "USA", "HUGE", "CEO", "NOOB", "MONEY", "WEEK", "YOLO", "LOW"
            };

        static void Main(string[] args)
        {
            //Create Mongo databases
            db = new MongoCRUD("YoutuberBook");

            var startTimeSpan = TimeSpan.Zero;
            var periodTimeSpan = TimeSpan.FromMinutes(1);

            Console.Write("Scanning....");

            //WRITE TO TEXT FILE 
            string docPath = AppDomain.CurrentDomain.BaseDirectory;

            using (StreamWriter outputFile = new StreamWriter(Path.Combine(docPath, "log.txt")))
            {
               outputFile.WriteLine("Scanning has started....");
            }

            //END OF TEXT FILE



            BeginMonitoring(db);
            Console.WriteLine("Last update at: " + DateTime.UtcNow.ToString("G"));


            using (StreamWriter outputFile = new StreamWriter(Path.Combine(docPath, "log.txt")))
            {
                outputFile.WriteLine("Scanning has FINISHED....");
            }

            //var timer = new System.Threading.Timer((e) =>
            //{

            //    BeginMonitoring(db);
            //    Console.WriteLine("Last update at: " + DateTime.UtcNow.ToString("G"));
            //}, null, startTimeSpan, periodTimeSpan);


            // _quitEvent.WaitOne();
        }


        public static void BeginMonitoring(MongoCRUD db)
        {
            //Settup new Chrome client with Selenium
            var options = new ChromeOptions();
            options.AddArguments("--disable-gpu");
            var chromeDriver = new ChromeDriver(options);

            //Create records for youtubers not in the database
            foreach (string ytr in youtubers)
            {
                if (db.LoadRecords<YoutuberModel>("Youtubers").Count == 0 || db.recordExists<YoutuberModel>("Youtubers", ytr) == false)
                    db.InsertRecord("Youtubers", CreateYoutuber(ytr));
            }


            //Update records of youtubers
            foreach (string ytr in youtubers)
            {
                var oneRec = db.LoadRecrdByUrl<YoutuberModel>("Youtubers", ytr);

                UpdateLatestPost(chromeDriver, oneRec);
                UpdateLatestVideo(chromeDriver, oneRec);

                
            }

            //Console.WriteLine("Name: " + oneRec.name + " Last Post: " + oneRec.latestPost);
            chromeDriver.Close();

        }

        public static void UpdateLatestPost(ChromeDriver chromeDriver, YoutuberModel youtuber)
        {
            string latestPost = string.Empty;
            string postTime = string.Empty;

            try
            {
                chromeDriver.Navigate().GoToUrl(youtuber.url + "/community");

                if (!chromeDriver.FindElementByCssSelector("#message").Text.Contains("posted yet"))
                {
                    latestPost = chromeDriver.FindElementByCssSelector("#content-text").Text;
                    postTime = chromeDriver.FindElementByCssSelector("#published-time-text > a").Text;

                    youtuber.latestPost = latestPost;
                    youtuber.postTime = postTime;
                    youtuber.tickersMentioned = ScanForTickersNew(latestPost);
                    
                    if(youtuber.latestPost != latestPost)
                    {
                        Console.WriteLine("New Post by: " + youtuber.name + " from " + postTime);
                        Console.WriteLine(latestPost);
                    }

                    youtuber.latestPost = latestPost;
                    youtuber.postTime = postTime;

                    db.UpdatePostRecord("Youtubers", youtuber, youtuber);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("Error: " + ex.InnerException);
            }

        }

        public static void UpdateLatestVideo(ChromeDriver chromeDriver, YoutuberModel youtuber)
        {
            string latestVideo = string.Empty;
            string videoTime = string.Empty;

            try
            {
                chromeDriver.Navigate().GoToUrl(youtuber.url + "/videos");

                latestVideo = chromeDriver.FindElementByCssSelector("#video-title").Text;
                videoTime = chromeDriver.FindElementByCssSelector("#metadata-line > span:nth-child(2)").Text;

                if (youtuber.latestVideo != latestVideo)
                {
                    Console.WriteLine("New Post by: " + youtuber.name + " from " + videoTime);
                    Console.WriteLine(latestVideo);
                }

                youtuber.tickersMentioned = ScanForTickersNew(latestVideo);
                youtuber.latestVideo = latestVideo;
                youtuber.videoTime = videoTime;

                db.UpdateVideoRecord("Youtubers", youtuber, youtuber);

            }
            catch (AggregateException ex)
            {
                Console.WriteLine("Error: " + ex.InnerException);
            }
        }

        public static YoutuberModel CreateYoutuber(string url)
        {
            string name = url.Split('/').Last();
            string latestPost = "";
            string latestVideo = "";

            return new YoutuberModel(name, url, latestPost, latestVideo);
        }

        public static List<StockModel> ScanForTickersNew(string text)
        {

            var cleanText = Regex.Replace(text, @"[^0-9a-zA-Z$]+", " ");
            var wordList = cleanText.Split(' ');

            List<string> tickerWords = wordList.Where(x => x.StartsWith("$") && !Regex.IsMatch(x, (".*\\d+.*"))).ToList<string>();

            List<StockModel> tickers = new List<StockModel>();

            foreach (string ticker in tickerWords)
            {
                string tickerText = ticker.TrimStart('$');

                if (!bannedWords.Contains(tickerText))
                    tickers.Add(CreateStock(tickerText));
            }
            return tickers;
        }

        public static StockModel CreateStock(string ticker)
        {
            //Check if ticker exists for youtuber? Update ticker discovery date -> aka match to latest video/post date?

            //ALSO add the ticker to new database with unique ticker info only

            return new StockModel(ticker, DateTime.Today.ToString("D"));
        }
    }

    public class YoutuberModel
    {
        public YoutuberModel(string name, string url, string latestPost, string latestVideo)
        {
            this.name = name;
            this.url = url;
            this.latestPost = latestPost;
            this.latestVideo = latestVideo;
        }

        [BsonId] //_id field in MongoDB
        public Guid Id { get; set; }

        public string name { get; set; }
        public string url { get; set; }
        public string latestPost { get; set; }
        public string postTime { get; set; }
        public string latestVideo { get; set; }
        public string videoTime { get; set; }

        public List<StockModel> tickersMentioned {get;set;}
    }

    public class StockModel
    {
        public StockModel(string ticker, string tickerFirstMention)
        {
            this.ticker = ticker;
            this.tickerFirstMention = tickerFirstMention;
        }

        public string ticker { get; set; }
        public string name { get; set; }
        public string price { get; set; }
        public string volume { get; set; }
        public string avgVolume { get; set; }
        public string min52 { get; set; }
        public string max52 { get; set; }

        //E.g When was this ticker mentioned by a youtuber 
        public string tickerFirstMention { get; set; }

    }

    public class MongoCRUD
    {
        private IMongoDatabase db;

        public MongoCRUD(string database)
        {
            var client = new MongoClient();
            db = client.GetDatabase(database);
        }

        public void InsertRecord<T>(string table, T record)
        {
            var collection = db.GetCollection<T>(table);
            collection.InsertOne(record);
        }

        public List<T> LoadRecords<T>(string table)
        {
            var collection = db.GetCollection<T>(table);

            return collection.Find(new BsonDocument()).ToList();
        }

        public bool recordExists<T>(string table, string url)
        {
            var collection = db.GetCollection<T>(table);
            var filter = Builders<T>.Filter.Eq("url", url);

            return collection.Find(filter).Any();
        }

        public T LoadRecrdByName<T>(string table, string name)
        {
            var collection = db.GetCollection<T>(table);
            var filter = Builders<T>.Filter.Eq("name", name);

            return collection.Find(filter).First();
        }

        public T LoadRecrdByUrl<T>(string table, string url)
        {
            var collection = db.GetCollection<T>(table);
            var filter = Builders<T>.Filter.Eq("url", url);

            return collection.Find(filter).First();
        }

        public void UpdateVideoRecord<T>(string table, YoutuberModel ytr, T record)
        {
            var collection = db.GetCollection<T>(table);
            var filter = Builders<T>.Filter.Eq("name", ytr.name);
            var update = Builders<T>.Update.Set("latestVideo", ytr.latestVideo);
            var timeUpdate = Builders<T>.Update.Set("videoTime", ytr.videoTime);
            var tickersMentioned = Builders<T>.Update.Set("tickersMentioned", ytr.tickersMentioned);

            //Update last video title
            collection.UpdateOne(
                filter,
                update,
                new UpdateOptions { IsUpsert = true });

            //Update last video time
            collection.UpdateOne(
                filter,
                timeUpdate,
                new UpdateOptions { IsUpsert = true });

            //Update tickers
            collection.UpdateOne(
                filter,
                tickersMentioned,
                new UpdateOptions { IsUpsert = true });
        }

        public void UpdatePostRecord<T>(string table, YoutuberModel ytr, T record)
        {
            var collection = db.GetCollection<T>(table);
            var filter = Builders<T>.Filter.Eq("name", ytr.name);
            var update = Builders<T>.Update.Set("latestPost", ytr.latestPost);
            var timeUpdate = Builders<T>.Update.Set("postTime", ytr.postTime);
            var tickersMentioned = Builders<T>.Update.Set("tickersMentioned", ytr.tickersMentioned);

            //Update last post
            collection.UpdateOne(
                filter,
                update,
                new UpdateOptions { IsUpsert = true });

            //Update last post time
            collection.UpdateOne(
                filter,
                timeUpdate,
                new UpdateOptions { IsUpsert = true });

            //Update tickers
            collection.UpdateOne(
                filter,
                tickersMentioned,
                new UpdateOptions { IsUpsert = true });
        }


    }
}
