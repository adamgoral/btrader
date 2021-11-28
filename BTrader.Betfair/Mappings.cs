using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BFSwagger = Betfair.ESASwagger;
using Api_ng_sample_code;
using Api_ng_sample_code.TO;
using BTrader.Domain;
using System.Text.RegularExpressions;
using Betfair.ESASwagger.Model;
using System.Collections.ObjectModel;

namespace BTrader.Betfair
{
    public static class Mappings
    {

        public static Domain.Order ToOrder(this CurrentOrderSummary source)
        {
            var side = Domain.OrderSide.Back;
            if(source.Side == Side.LAY)
            {
                side = Domain.OrderSide.Lay;
            }

            var status = Domain.OrderStatus.Open;
            if(source.Status == Api_ng_sample_code.TO.OrderStatus.EXECUTION_COMPLETE)
            {
                if(source.PriceSize.Size == source.SizeMatched)
                {
                    status = Domain.OrderStatus.Filled;
                }
                else
                {
                    status = Domain.OrderStatus.Cancelled;
                }
            }

            return new Domain.Order(source.BetId, source.MarketId, source.SelectionId, source.PlacedDate, side, status, (decimal)source.PriceSize.Price, (decimal)source.PriceSize.Size, (decimal)source.SizeMatched);
        }

        public static IEnumerable<Domain.Order> ToOrders(this OrderMarketChange change)
        {
            if (change.Orc == null) yield break;

            foreach(var outcomeOrderChange in change.Orc)
            {
                foreach(var order in outcomeOrderChange.ToOrders(change.Id))
                {
                    yield return order;
                }
            }
        }

        public static IEnumerable<Domain.Order> ToOrders(this OrderRunnerChange change, string marketId)
        {
            if (change.Uo == null) yield break;
            foreach(var order in change.Uo)
            {
                var createdOn = new DateTime(1970, 1, 1).AddMilliseconds(order.Pd.Value);
                var side = OrderSide.Back;
                if(order.Side == BFSwagger.Model.Order.SideEnum.L)
                {
                    side = OrderSide.Lay;
                }

                var status = Domain.OrderStatus.Open;
                if(order.Status == BFSwagger.Model.Order.StatusEnum.Ec)
                {
                    if(order.Sm == order.S)
                    {
                        status = Domain.OrderStatus.Filled;
                    }
                    else
                    {
                        status = Domain.OrderStatus.Cancelled;
                    }
                }

                yield return new Domain.Order(order.Id, marketId, change.Id.Value.ToString(), createdOn, side, status, (decimal)order.P.Value, (decimal)order.S.Value, (decimal) (order.Sm ?? 0));
            }
        }

        public static MarketObservation ToMarketObservation(this BFSwagger.Model.MarketChange marketChange, DateTime timeStamp)
        {
            string name = null;
            var status = Domain.MarketStatus.Open;
            string type = null;
            DateTime? suspendTime = null;

            if (marketChange.MarketDefinition != null)
            {
                if (marketChange.MarketDefinition.Status == BFSwagger.Model.MarketDefinition.StatusEnum.Suspended ||
                    marketChange.MarketDefinition.Status == BFSwagger.Model.MarketDefinition.StatusEnum.Inactive)
                {
                    status = Domain.MarketStatus.Suspended;
                }
                else if (marketChange.MarketDefinition.Status == BFSwagger.Model.MarketDefinition.StatusEnum.Closed)
                {
                    status = Domain.MarketStatus.Closed;
                }
                suspendTime = marketChange.MarketDefinition.SuspendTime;
            }

            var outcomeObservations = new List<OutcomeObservation>();

            if (marketChange.Rc != null)
            {
                foreach (var r in marketChange.Rc)
                {
                    decimal? totalTraded = null;
                    if (r.Tv != null)
                    {
                        totalTraded = (decimal)r.Tv;
                    }

                    var orderBook = new OrderBookObservation(r.Atl.ToPriceSize(), r.Atb.ToPriceSize(), r.Trd.ToPriceSize(), totalTraded, marketChange.Img == true);
                    var outcomeObservation = new OutcomeObservation(r.Id.ToString(), timeStamp, null, null, orderBook, new Domain.Order[0], null, false);
                    outcomeObservations.Add(outcomeObservation);
                }
            }
            decimal? totalVolume = null;
            if (marketChange.Tv != null)
            {
                totalVolume = (decimal)marketChange.Tv;
            }

            var result = new MarketObservation(marketChange.Id, suspendTime, timeStamp, name, status, type, totalVolume, outcomeObservations);
            return result;
        }

        public static IEnumerable<Domain.PriceSize> ToPriceSize(this IEnumerable<IList<double?>> source)
        {
            if (source == null) yield break;
            foreach (var item in source)
            {
                yield return new Domain.PriceSize((decimal)item[0], (decimal)item[1]);
            }
        }

        public static IEnumerable<Domain.PriceSize> ToPriceSize(this IEnumerable<Api_ng_sample_code.TO.PriceSize> source)
        {
            if (source == null) return new Domain.PriceSize[0];
            return source.Select(p => new Domain.PriceSize((decimal)p.Price, (decimal)p.Size));
        }

        public static IEnumerable<OutcomeObservation> ToOutcomeObservation(this IEnumerable<RunnerDescription> source, MarketBook marketBook, DateTime timestamp)
        {
            var runnerNameRegex = new Regex(@"\d*\. ");
            var runnerBookLookup = new Dictionary<long, Runner>();
            if (marketBook != null)
            {
                runnerBookLookup = marketBook.Runners.ToDictionary(i => i.SelectionId, i => i);
            };

            foreach (var description in source)
            {
                var runnerName = description.RunnerName;
                if (runnerNameRegex.IsMatch(runnerName))
                {
                    runnerName = runnerName.Substring(runnerName.IndexOf('.') + 1).Trim();
                }

                var status = OutcomeStatus.Active;
                Runner runner = null;
                if (runnerBookLookup.TryGetValue(description.SelectionId, out runner))
                {
                    if (runner.Status == RunnerStatus.LOSER)
                    {
                        status = OutcomeStatus.Loser;
                    }
                    else if (runner.Status == RunnerStatus.WINNER)
                    {
                        status = OutcomeStatus.Winner;
                    }
                    else if (runner.Status == RunnerStatus.REMOVED || runner.Status == RunnerStatus.REMOVED_VACANT)
                    {
                        status = OutcomeStatus.Removed;
                    }
                }

                var orderBook = new OrderBookObservation(runner?.ExchangePrices.AvailableToLay.ToPriceSize(), runner?.ExchangePrices.AvailableToBack.ToPriceSize(), runner?.ExchangePrices.TradedVolume.ToPriceSize(), (decimal?)runner?.TotalMatched, true);
                var result = new OutcomeObservation(description.SelectionId.ToString(), timestamp, runnerName, status, orderBook, new Domain.Order[0], (decimal)description.Handicap, false);
                yield return result;
            }
        }

        public static ReadOnlyDictionary<string, OrderBookObservation> ToOrderBookObservation(this MarketBook marketBook)
        {
            var result = new Dictionary<string, OrderBookObservation>();
            foreach (var runner in marketBook.Runners)
            {
                var orderBook = new OrderBookObservation(runner.ExchangePrices.AvailableToLay.ToPriceSize(), runner.ExchangePrices.AvailableToBack.ToPriceSize(), runner.ExchangePrices.TradedVolume.ToPriceSize(), (decimal?)runner.TotalMatched, true);
                var key = runner.SelectionId.ToString();
                if (runner.Handicap != null && runner.Handicap != 0)
                {
                    key += $":{runner.Handicap}";
                }

                result[key] = orderBook;
            }

            return new ReadOnlyDictionary<string, OrderBookObservation>(result);
        }

        public static IEnumerable<MarketObservation> ToMarketObservation(this IEnumerable<MarketCatalogue> source, Dictionary<string, MarketBook> marketBooks, DateTime timestamp)
        {
            foreach (var item in source)
            {
                var status = Domain.MarketStatus.Open;

                MarketBook marketBook = null;
                if (marketBooks.TryGetValue(item.MarketId, out marketBook))
                {
                    if (marketBook.Status == Api_ng_sample_code.TO.MarketStatus.CLOSED)
                    {
                        status = Domain.MarketStatus.Closed;
                    }
                    else if (marketBook.Status == Api_ng_sample_code.TO.MarketStatus.SUSPENDED || marketBook.Status == Api_ng_sample_code.TO.MarketStatus.INACTIVE)
                    {
                        status = Domain.MarketStatus.Suspended;
                    }
                }

                var result = new MarketObservation(item.MarketId, item.Description.SuspendTime.Value, timestamp, item.MarketName, status, item.Description.MarketType, (decimal?)marketBook?.TotalMatched, item.Runners.ToOutcomeObservation(marketBook, timestamp));
                yield return result;
            }
        }

        public static IEnumerable<Domain.Order> ToOrders(this IEnumerable<PlaceInstructionReport> placeInstructionReports, string marketId)
        {
            return placeInstructionReports.Where(r => r.Status == InstructionReportStatus.SUCCESS).Select(r => r.ToOrder(marketId));
        }

        public static Domain.Order ToOrder(this PlaceInstructionReport report, string marketId)
        {
            var side = OrderSide.Back;
            if(report.Instruction.Side == Side.LAY)
            {
                side = OrderSide.Lay;
            }

            var status = Domain.OrderStatus.Open;
            var limitOrder = report.Instruction.LimitOrder;
            var sizeMatched = 0M;
            if (report.SizeMatched != null)
            {
                sizeMatched = (decimal)report.SizeMatched.Value;
                if(report.SizeMatched == limitOrder.Size)
                {
                    status = Domain.OrderStatus.Filled;
                }
            }
            var outcomeId = report.Instruction.SelectionId.ToString();
            var result = new Domain.Order(report.BetId, marketId, outcomeId, report.PlacedDate.Value, side, status, (decimal)limitOrder.Price, (decimal)limitOrder.Size, sizeMatched);
            return result;
        }
    }
}
