using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SlackNextV2
{
    public static class Function1
    {
        [FunctionName("Next-Farar-Meeting")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "foo")] HttpRequest req,
             [Table("Next", Connection = "StorageConnectionAppSetting")] CloudTable myTable,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string text = req.Form["text"];
            var now = DateTime.Now;
            var rowKey = now.ToString("yyyyMM");
            string fullMonthName = GetFullMonthName(now);
            var currentRows = await FindFromThisMonth(myTable, rowKey);

            // /next change 202006|06 jarle
            if (text.StartsWith("change") || text.StartsWith("c"))
            {
                var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                rowKey = args[1];
                var target = args[2];

                if (rowKey.Length == 1)
                {
                    rowKey = "0" + rowKey;
                }

                if (rowKey.Length == 2)
                {
                    // assume this year
                    rowKey = DateTime.Now.Year.ToString() + rowKey;
                }

                fullMonthName = GetFullMonth(rowKey);
                var nextInfo = new MyPoco
                {
                    PartitionKey = "next",
                    RowKey = rowKey,
                    MonthName = fullMonthName,
                    Text = target
                };
                await myTable.ExecuteAsync(TableOperation.InsertOrReplace(nextInfo));
                var r = new Response
                {
                    text = $"{nextInfo.Text} set as host for: {nextInfo.MonthName}",
                    response_type = "in_channel"
                };

                return new OkObjectResult(r);
            }

            if (text.StartsWith("queue") || text.StartsWith("q"))
            {
                if (currentRows.Any())
                {
                    var lastRow = currentRows.OrderBy(p => p.RowKey).Last();
                    string last = lastRow == null ? rowKey : lastRow.RowKey;
                    var lastYear = int.Parse(last.Substring(0, 4));
                    var lastMonth = int.Parse(last.Substring(4, 2));

                    if (lastMonth == 12)
                    {
                        lastYear++;
                        lastMonth = 1;
                    }
                    else
                    {
                        lastMonth++;
                    }

                    var futureDate = new DateTime(lastYear, lastMonth, 1);
                    fullMonthName = futureDate.ToString("MMMM", CultureInfo.CreateSpecificCulture("nb-no"));
                    rowKey = futureDate.ToString("yyyyMM");
                }

                var nextInfo = new MyPoco
                {
                    PartitionKey = "next",
                    RowKey = rowKey,
                    MonthName = fullMonthName,
                    Text = text.Replace("queue ", "").Replace("q ", "")
                };
                await myTable.ExecuteAsync(TableOperation.Insert(nextInfo));
                var r = new Response
                {
                    text = $"{nextInfo.Text} added as next, month: {nextInfo.MonthName}",
                    response_type = "in_channel"
                };

                return new OkObjectResult(r);
            };
            if (currentRows.Count() == 0)
            {
                var r = new Response
                {
                    text = $"Empty queue! Please queue some more meets - /next q Meeting-Responsible",
                    response_type = "in_channel"
                };
                return new OkObjectResult(r);
            }

            MyPoco myPoco = currentRows.First();
            var resp = new Response
            {
                text = $"Next is: {myPoco.Text} in {myPoco.MonthName}",
                response_type = "in_channel"
            };
            return new OkObjectResult(resp);
        }

        private static string GetFullMonth(string rowKey)
        {
            var year = int.Parse(rowKey.Substring(0, 4));
            var monthString = rowKey.Substring(4, 2);
            if (monthString.StartsWith("0")) monthString = monthString.Substring(1, 1);
            var month = int.Parse(monthString);
            var targetDate = new DateTime(year, month, 1);
            return GetFullMonthName(targetDate);
        }

        private static string GetFullMonthName(DateTime now)
        {
            return now.ToString("MMMM", CultureInfo.CreateSpecificCulture("nb-no"));
        }

        private static async Task<List<MyPoco>> FindFromThisMonth(CloudTable cloudTable, string currentMonth)
        {
            var prefixCondition = TableQuery
                .GenerateFilterCondition("RowKey", QueryComparisons.GreaterThanOrEqual, currentMonth);

            var filterString = TableQuery.CombineFilters(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "next"),
                TableOperators.And, prefixCondition);
            TableQuery<MyPoco> query = new TableQuery<MyPoco>();
            query.FilterString = filterString;

            TableContinuationToken token = null;
            var queue = new List<MyPoco>();
            do
            {
                var res = await cloudTable.ExecuteQuerySegmentedAsync<MyPoco>(query, token);

                token = res.ContinuationToken;
                queue.AddRange(res.Results);

            } while (token != null);

            return queue;
        }

        public class MyPoco : TableEntity
        {
            public string Text { get; set; }
            public string MonthName { get; set; }
        }


        public class Response
        {
            public string response_type { get; set; }
            public string text { get; set; }
        }

    }
}
