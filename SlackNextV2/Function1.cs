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
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "foo")] HttpRequest req,
             [Table("Next", Connection = "StorageConnectionAppSetting")] CloudTable myTable,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string text = req.Form["text"];
            var rowKey = DateTime.Now.ToString("yyyyMM");
            string fullMonthName = DateTime.Now.ToString("MMMM", CultureInfo.CreateSpecificCulture("nb-no"));
            var currentRows = await FindFromThisMonth(myTable, rowKey);

            // /next queue @jarle
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

            MyPoco myPoco = currentRows.First();
            var resp = new Response
            {
                text = $"Next is: {myPoco.Text} in {myPoco.MonthName}",
                response_type = "in_channel"
            };
            return new OkObjectResult(resp);
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
