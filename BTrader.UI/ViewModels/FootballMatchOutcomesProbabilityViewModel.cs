using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Reactive.Linq;
using System.Collections.ObjectModel;

namespace BTrader.UI.ViewModels
{
    public class FootballMatchOutcomesProbabilityViewModel : BaseViewModel
    {
        private IObservable<EventNavItem> eventSelection;
        private Dictionary<string, ISession> sessions;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private EventNavItem eventNavItem;
        private string selectedPriceField = "ToLay";

        public string[] PriceFields { get; } = new[] { "ToLay", "ToBack" };

        public FootballMatchOutcomesProbabilityViewModel(IObservable<EventNavItem> eventSelection, Dictionary<string, ISession> sessions, Dispatcher dispatcher)
        {
            this.eventSelection = eventSelection;
            this.sessions = sessions;
            this.disposables.Add(eventSelection.ObserveOn(dispatcher).Subscribe(this.OnEventSelected));
            this.RefreshCommand = new DelegateCommand(o => true, o => this.Refresh());
        }

        public DelegateCommand RefreshCommand { get; private set; }

        private void Refresh()
        {
            this.OnEventSelected(this.eventNavItem);
        }

        public string SelectedPriceField
        {
            get => this.selectedPriceField;
            set
            {
                if(this.selectedPriceField != value)
                {
                    this.selectedPriceField = value;
                    this.OnPropertyChanged("SelectedPriceField");
                    this.Refresh();
                }
            }
        }

        private void OnEventSelected(EventNavItem e)
        {
            this.eventNavItem = e;
            this.OutcomePayOffs.Clear();
            var sessionId = "Betfair";
            var footballMatch = e.Events[sessionId];
            var session = this.sessions[sessionId];
            var latestPrices = session.GetObservations(footballMatch.Markets.Keys);
            var probabilities = GetScoreProbabilities(footballMatch, latestPrices).ToArray();
            if (!probabilities.Any()) return;
            var maxGoals = Math.Max(probabilities.Where(o => o.Score != null).Max(o => o.Score.Home), probabilities.Where(o => o.Score != null).Max(o => o.Score.Away));
            var probGrid = new decimal?[maxGoals+1, maxGoals+1];
            foreach(var p in probabilities.Where(p => p.Score != null))
            {
                probGrid[p.Score.Home, p.Score.Away] = p.Probability;
            }

            for (var i = 0; i <= maxGoals; i++)
            {
                for(var j = 0; j <= maxGoals; j++)
                {
                    if(probGrid[i,j] == null)
                    {
                        return;
                    }
                }
            }
            var otherProbs = probabilities.Where(p => p.OtherScore != null).ToDictionary(p => p.OtherScore.Winnner, p => p.Probability);
            if (otherProbs.Count != 3) return;
            var probs = new OutcomePayOffSetItem
            {
                Name = "Probabilities",
                OtherWin_H = otherProbs.ContainsKey("H") ? (decimal?)otherProbs["H"] : null,
                OtherWin_A = otherProbs.ContainsKey("A") ? (decimal?)otherProbs["A"] : null,
                OtherDraw = otherProbs.ContainsKey("D") ? (decimal?)otherProbs["D"] : null
            };

            probs.Scores = probGrid;

            var useBestToBackPrice = true;
            if(this.selectedPriceField == "ToLay")
            {
                useBestToBackPrice = false;
            }

            this.OutcomePayOffs.Add(probs);

            foreach(var payoff in this.GetScorePayoffs(footballMatch, latestPrices, probGrid, otherProbs, useBestToBackPrice))
            {
                this.OutcomePayOffs.Add(payoff);
            }

            foreach (var payoff in this.GetMatchOddsPayoffs(footballMatch, latestPrices, probGrid, otherProbs, useBestToBackPrice))
            {
                this.OutcomePayOffs.Add(payoff);
            }

            var totalGoalPayoff = this.GetTotalGoalsPayoff(footballMatch, latestPrices, probGrid, otherProbs, 0.5M, "OVER_UNDER_05", useBestToBackPrice);
            if (totalGoalPayoff != null) this.OutcomePayOffs.Add(totalGoalPayoff);
            totalGoalPayoff = this.GetTotalGoalsPayoff(footballMatch, latestPrices, probGrid, otherProbs, 1.5M, "OVER_UNDER_15", useBestToBackPrice);
            if (totalGoalPayoff != null) this.OutcomePayOffs.Add(totalGoalPayoff);
            totalGoalPayoff = this.GetTotalGoalsPayoff(footballMatch, latestPrices, probGrid, otherProbs, 2.5M, "OVER_UNDER_25", useBestToBackPrice);
            if (totalGoalPayoff != null) this.OutcomePayOffs.Add(totalGoalPayoff);
            totalGoalPayoff = this.GetTotalGoalsPayoff(footballMatch, latestPrices, probGrid, otherProbs, 3.5M, "OVER_UNDER_35", useBestToBackPrice);
            if (totalGoalPayoff != null) this.OutcomePayOffs.Add(totalGoalPayoff);

            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == "MATCH_ODDS");
            var outcomes = matchOdds.Outcomes.ToArray();
            var homeTeam = outcomes[0].Name;
            var awayTeam = outcomes[1].Name;

            foreach (var payoff in this.GetAsianH0Payoff(footballMatch, latestPrices, probGrid, otherProbs, homeTeam, awayTeam, useBestToBackPrice))
            {
                this.OutcomePayOffs.Add(payoff);
            }

            foreach (var payoff in this.GetAsianHMinus025Payoff(footballMatch, latestPrices, probGrid, otherProbs, homeTeam, awayTeam, useBestToBackPrice))
            {
                this.OutcomePayOffs.Add(payoff);
            }
            foreach (var payoff in this.GetAsianHPlus025Payoff(footballMatch, latestPrices, probGrid, otherProbs, homeTeam, awayTeam, useBestToBackPrice))
            {
                this.OutcomePayOffs.Add(payoff);
            }
            foreach (var payoff in this.GetAsianHMinus05Payoff(footballMatch, latestPrices, probGrid, otherProbs, homeTeam, awayTeam, useBestToBackPrice))
            {
                this.OutcomePayOffs.Add(payoff);
            }
            foreach (var payoff in this.GetAsianHPlus05Payoff(footballMatch, latestPrices, probGrid, otherProbs, homeTeam, awayTeam, useBestToBackPrice))
            {
                this.OutcomePayOffs.Add(payoff);
            }
        }

        public IEnumerable<OutcomePayOffSetItem> GetAsianHMinus05Payoff(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, string homeTeam, string awayTeam, bool useBestToBackPrice)
        {
            decimal handicap = -0.5M;
            var marketType = "ASIAN_HANDICAP";
            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == marketType);
            if (matchOdds != null)
            {
                var prices = latestPrices[matchOdds.Id];
                var outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == homeTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = -1M;
                                    if (i > j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i < j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = -1M * otherProbs["D"];
                            var otherWinHPayoff = (priceToUse - 1M) * otherProbs["H"];
                            var otherWinAPayoff = -1 * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }

                outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == awayTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = -1M;
                                    if (i < j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i > j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = -1M * otherProbs["D"];
                            var otherWinHPayoff = -1 * otherProbs["H"];
                            var otherWinAPayoff = (priceToUse - 1M) * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }
            }
        }

        public IEnumerable<OutcomePayOffSetItem> GetAsianHPlus05Payoff(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, string homeTeam, string awayTeam, bool useBestToBackPrice)
        {
            decimal handicap = 0.5M;
            var marketType = "ASIAN_HANDICAP";
            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == marketType);
            if (matchOdds != null)
            {
                var prices = latestPrices[matchOdds.Id];
                var outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == homeTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = priceToUse - 1M;
                                    if (i > j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i < j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = (priceToUse - 1M) * otherProbs["D"];
                            var otherWinHPayoff = (priceToUse - 1M) * otherProbs["H"];
                            var otherWinAPayoff = -1 * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }

                outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == awayTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = priceToUse - 1M;
                                    if (i < j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i > j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = (priceToUse - 1M) * otherProbs["D"];
                            var otherWinHPayoff = -1 * otherProbs["H"];
                            var otherWinAPayoff = (priceToUse - 1M) * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }
            }
        }

        public IEnumerable<OutcomePayOffSetItem> GetAsianHPlus025Payoff(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, string homeTeam, string awayTeam, bool useBestToBackPrice)
        {
            decimal handicap = 0.25M;
            var marketType = "ASIAN_HANDICAP";
            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == marketType);
            if (matchOdds != null)
            {
                var prices = latestPrices[matchOdds.Id];
                var outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == homeTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = (priceToUse - 1M) * 0.5M;
                                    if (i > j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i < j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = (priceToUse - 1M) * 0.5M * otherProbs["D"];
                            var otherWinHPayoff = (priceToUse - 1M) * otherProbs["H"];
                            var otherWinAPayoff = -1 * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }

                outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == awayTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = (priceToUse - 1M) * 0.5M;
                                    if (i < j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i > j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = (priceToUse - 1M) * 0.5M * otherProbs["D"];
                            var otherWinHPayoff = -1 * otherProbs["H"];
                            var otherWinAPayoff = (priceToUse - 1M) * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }
            }
        }


        public IEnumerable<OutcomePayOffSetItem> GetAsianHMinus025Payoff(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, string homeTeam, string awayTeam, bool useBestToBackPrice)
        {
            decimal handicap = -0.25M;
            var marketType = "ASIAN_HANDICAP";
            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == marketType);
            if (matchOdds != null)
            {
                var prices = latestPrices[matchOdds.Id];
                var outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == homeTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = -0.5M;
                                    if (i > j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i < j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = -0.5M * otherProbs["D"];
                            var otherWinHPayoff = (priceToUse - 1M) * otherProbs["H"];
                            var otherWinAPayoff = -1 * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }

                outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == awayTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id + $":{handicap}", out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = -0.5M;
                                    if (i < j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i > j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = -0.5M * otherProbs["D"];
                            var otherWinHPayoff = -1 * otherProbs["H"];
                            var otherWinAPayoff = (priceToUse - 1M) * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }
            }
        }

        public IEnumerable<OutcomePayOffSetItem> GetAsianH0Payoff(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, string homeTeam, string awayTeam, bool useBestToBackPrice)
        {
            decimal handicap = 0;
            var marketType = "ASIAN_HANDICAP";
            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == marketType);
            if (matchOdds != null)
            {
                var prices = latestPrices[matchOdds.Id];
                var outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == homeTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id, out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = 0M;
                                    if (i > j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if(i < j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = 0 * otherProbs["D"];
                            var otherWinHPayoff = (priceToUse - 1M) * otherProbs["H"];
                            var otherWinAPayoff = -1 * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }

                outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Handicap == handicap && e.Name == awayTeam);
                if (outcome != null)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id, out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                        {
                            var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                            var priceToUse = bestToLay;
                            if (useBestToBackPrice)
                            {
                                priceToUse = bestToBack;
                            }

                            decimal? expectedPayoff = 0M;
                            for (var i = 0; i < probGrid.GetLength(0); i++)
                            {
                                for (var j = 0; j < probGrid.GetLength(1); j++)
                                {
                                    var payoff = 0M;
                                    if (i < j)
                                    {
                                        payoff = priceToUse - 1M;
                                    }
                                    else if (i > j)
                                    {
                                        payoff = -1;
                                    }

                                    scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                    expectedPayoff += scorePayoffGrid[i, j];
                                }
                            }

                            var otherDrawPayoff = 0 * otherProbs["D"];
                            var otherWinHPayoff = -1 * otherProbs["H"];
                            var otherWinAPayoff = (priceToUse - 1M) * otherProbs["A"];
                            expectedPayoff += otherDrawPayoff;
                            expectedPayoff += otherWinHPayoff;
                            expectedPayoff += otherWinAPayoff;
                            var payoffSet = new OutcomePayOffSetItem
                            {
                                Name = outcome.Name + $" h{handicap}",
                                BestToBack = bestToBack,
                                BestToLay = bestToLay,
                                TradedVolume = outcomePrices.Volume,
                                ExpectedPayoff = expectedPayoff,
                                OtherDraw = otherDrawPayoff,
                                OtherWin_A = otherWinAPayoff,
                                OtherWin_H = otherWinHPayoff,
                                Scores = scorePayoffGrid
                            };

                            yield return payoffSet;
                        }
                    }
                }
            }
        }


        public OutcomePayOffSetItem GetTotalGoalsPayoff(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, decimal maxGoals, string marketType, bool useBestToBackPrice)
        {
            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == marketType);
            if (matchOdds != null)
            {
                var prices = latestPrices[matchOdds.Id];
                var outcome = matchOdds.Outcomes.FirstOrDefault(e => e.Name.StartsWith("Under"));
                if (outcome == null) return null;
                OrderBookObservation outcomePrices;
                if (prices.TryGetValue(outcome.Id, out outcomePrices))
                {
                    if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                    {
                        var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                        var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                        var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                        var priceToUse = bestToLay;
                        if (useBestToBackPrice)
                        {
                            priceToUse = bestToBack;
                        }

                        decimal? expectedPayoff = 0M;
                        for (var i = 0; i < probGrid.GetLength(0); i++)
                        {
                            for (var j = 0; j < probGrid.GetLength(1); j++)
                            {
                                var payoff = -1M;
                                if (i + j < maxGoals)
                                {
                                    payoff = priceToUse - 1M;
                                }

                                scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                expectedPayoff += scorePayoffGrid[i, j];
                            }
                        }

                        var otherDrawPayoff = -1 * otherProbs["D"];
                        var otherWinHPayoff = -1 * otherProbs["H"];
                        var otherWinAPayoff = -1 * otherProbs["A"];
                        expectedPayoff += otherDrawPayoff;
                        expectedPayoff += otherWinHPayoff;
                        expectedPayoff += otherWinAPayoff;
                        var payoffSet = new OutcomePayOffSetItem
                        {
                            Name = outcome.Name,
                            BestToBack = bestToBack,
                            BestToLay = bestToLay,
                            TradedVolume = outcomePrices.Volume,
                            ExpectedPayoff = expectedPayoff,
                            OtherDraw = otherDrawPayoff,
                            OtherWin_A = otherWinAPayoff,
                            OtherWin_H = otherWinHPayoff,
                            Scores = scorePayoffGrid
                        };

                        return payoffSet;
                    }
                }
            }

            return null;
        }

        private IEnumerable<OutcomePayOffSetItem> GetMatchOddsPayoffs(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, bool useBestToBackPrice)
        {
            var matchOdds = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == "MATCH_ODDS");
            if(matchOdds != null)
            {
                var prices = latestPrices[matchOdds.Id];
                var outcome = matchOdds.Outcomes.ToArray()[0];
                OrderBookObservation outcomePrices;
                if (prices.TryGetValue(outcome.Id, out outcomePrices))
                {
                    if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                    {
                        var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                        var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                        var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                        var priceToUse = bestToLay;
                        if (useBestToBackPrice)
                        {
                            priceToUse = bestToBack;
                        }

                        decimal? expectedPayoff = 0M;
                        for (var i = 0; i < probGrid.GetLength(0); i++)
                        {
                            for (var j = 0; j < probGrid.GetLength(1); j++)
                            {
                                var payoff = -1M;
                                if (i > j)
                                {
                                    payoff = priceToUse - 1M;
                                }

                                scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                expectedPayoff += scorePayoffGrid[i, j];
                            }
                        }

                        var otherDrawPayoff = -1 * otherProbs["D"];
                        var otherWinHPayoff = (priceToUse - 1M) * otherProbs["H"];
                        var otherWinAPayoff = -1 * otherProbs["A"];
                        expectedPayoff += otherDrawPayoff;
                        expectedPayoff += otherWinHPayoff;
                        expectedPayoff += otherWinAPayoff;
                        var payoffSet = new OutcomePayOffSetItem
                        {
                            Name = outcome.Name,
                            BestToBack = bestToBack,
                            BestToLay = bestToLay,
                            TradedVolume = outcomePrices.Volume,
                            ExpectedPayoff = expectedPayoff,
                            OtherDraw = otherDrawPayoff,
                            OtherWin_A = otherWinAPayoff,
                            OtherWin_H = otherWinHPayoff,
                            Scores = scorePayoffGrid
                        };
                        yield return payoffSet;
                    }
                }


                outcome = matchOdds.Outcomes.ToArray()[1];
                if (prices.TryGetValue(outcome.Id, out outcomePrices))
                {
                    if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                    {
                        var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                        var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                        var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                        var priceToUse = bestToLay;
                        if (useBestToBackPrice)
                        {
                            priceToUse = bestToBack;
                        }

                        decimal? expectedPayoff = 0M;
                        for (var i = 0; i < probGrid.GetLength(0); i++)
                        {
                            for (var j = 0; j < probGrid.GetLength(1); j++)
                            {
                                var payoff = -1M;
                                if (i < j)
                                {
                                    payoff = priceToUse - 1M;
                                }

                                scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                expectedPayoff += scorePayoffGrid[i, j];
                            }
                        }

                        var otherDrawPayoff = -1 * otherProbs["D"];
                        var otherWinHPayoff = -1 * otherProbs["H"];
                        var otherWinAPayoff = (priceToUse - 1M) * otherProbs["A"];
                        expectedPayoff += otherDrawPayoff;
                        expectedPayoff += otherWinHPayoff;
                        expectedPayoff += otherWinAPayoff;
                        var payoffSet = new OutcomePayOffSetItem
                        {
                            Name = outcome.Name,
                            BestToBack = bestToBack,
                            BestToLay = bestToLay,
                            TradedVolume = outcomePrices.Volume,
                            ExpectedPayoff = expectedPayoff,
                            OtherDraw = otherDrawPayoff,
                            OtherWin_A = otherWinAPayoff,
                            OtherWin_H = otherWinHPayoff,
                            Scores = scorePayoffGrid
                        };
                        yield return payoffSet;
                    }
                }

                outcome = matchOdds.Outcomes.ToArray()[2];
                if (prices.TryGetValue(outcome.Id, out outcomePrices))
                {
                    if (outcomePrices.ToBack.Any() && outcomePrices.ToLay.Any())
                    {
                        var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                        var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                        var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                        var priceToUse = bestToLay;
                        if (useBestToBackPrice)
                        {
                            priceToUse = bestToBack;
                        }

                        decimal? expectedPayoff = 0M;
                        for (var i = 0; i < probGrid.GetLength(0); i++)
                        {
                            for (var j = 0; j < probGrid.GetLength(1); j++)
                            {
                                var payoff = -1M;
                                if (i == j)
                                {
                                    payoff = priceToUse - 1M;
                                }

                                scorePayoffGrid[i, j] = payoff * probGrid[i, j];
                                expectedPayoff += scorePayoffGrid[i, j];
                            }
                        }

                        var otherDrawPayoff = (priceToUse - 1M) * otherProbs["D"];
                        var otherWinHPayoff = -1 * otherProbs["H"];
                        var otherWinAPayoff = -1 * otherProbs["A"];
                        expectedPayoff += otherDrawPayoff;
                        expectedPayoff += otherWinHPayoff;
                        expectedPayoff += otherWinAPayoff;
                        var payoffSet = new OutcomePayOffSetItem
                        {
                            Name = outcome.Name,
                            BestToBack = bestToBack,
                            BestToLay = bestToLay,
                            TradedVolume = outcomePrices.Volume,
                            ExpectedPayoff = expectedPayoff,
                            OtherDraw = otherDrawPayoff,
                            OtherWin_A = otherWinAPayoff,
                            OtherWin_H = otherWinHPayoff,
                            Scores = scorePayoffGrid
                        };
                        yield return payoffSet;
                    }
                }
            }
        }

        private IEnumerable<OutcomePayOffSetItem> GetScorePayoffs(Event footballMatch, ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices, decimal?[,] probGrid, Dictionary<string, decimal> otherProbs, bool useBestToBackPrice)
        {
            var scores = footballMatch.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == "CORRECT_SCORE");
            if (scores != null)
            {
                var prices = latestPrices[scores.Id];
                foreach (var outcome in scores.Outcomes)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id, out outcomePrices))
                    {
                        if (outcomePrices.ToBack.Count > 0 && outcomePrices.ToLay.Count > 0)
                        {
                            Score score;
                            if (Score.TryParse(outcome.Name, out score))
                            {
                                var bestToBack = outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                                var bestToLay = outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                                var scorePayoffGrid = new decimal?[probGrid.GetLength(0), probGrid.GetLength(1)];
                                var priceToUse = bestToLay;
                                if (useBestToBackPrice)
                                {
                                    priceToUse = bestToBack;
                                }

                                decimal? expectedPayoff = 0M;
                                for (var i=0; i<probGrid.GetLength(0); i++)
                                {
                                    for(var j = 0; j < probGrid.GetLength(1); j++)
                                    {
                                        var payoff = -1M;
                                        if(i == score.Home && j == score.Away)
                                        {
                                            payoff = priceToUse - 1M;
                                        }

                                        scorePayoffGrid[i, j] = payoff * probGrid[i,j];
                                        expectedPayoff += scorePayoffGrid[i, j];
                                    }
                                }

                                var otherDrawPayoff = -1 * otherProbs["D"];
                                var otherWinHPayoff = -1 * otherProbs["H"];
                                var otherWinAPayoff = -1 * otherProbs["A"];
                                expectedPayoff += otherDrawPayoff;
                                expectedPayoff += otherWinHPayoff;
                                expectedPayoff += otherWinAPayoff;
                                var payoffSet = new OutcomePayOffSetItem
                                {
                                    Name = outcome.Name,
                                    BestToBack = bestToBack,
                                    BestToLay = bestToLay,
                                    TradedVolume = outcomePrices.Volume,
                                    ExpectedPayoff = expectedPayoff,
                                    OtherDraw = otherDrawPayoff,
                                    OtherWin_A = otherWinAPayoff,
                                    OtherWin_H = otherWinHPayoff,
                                    Scores = scorePayoffGrid
                                };

                                yield return payoffSet;
                            }
                        }
                    }
                }
            }
        }

        public ObservableCollection<OutcomePayOffSetItem> OutcomePayOffs { get; } = new ObservableCollection<OutcomePayOffSetItem>();

        public class OutcomePayOffSetItem
        {
            public string Name { get; set; }
            public decimal? BestToBack { get; set; }
            public decimal? BestToLay { get; set; }
            public decimal? TradedVolume { get; set; }

            public decimal? ExpectedPayoff { get; set; }

            public decimal?[,] Scores { get; set; } = new decimal?[4, 4];

            public decimal? Score0_0 => this.Scores[0, 0];
            public decimal? Score0_1 => this.Scores[0, 1];
            public decimal? Score0_2 => this.Scores[0, 2];
            public decimal? Score0_3 => this.Scores[0, 3];
            public decimal? Score1_0 => this.Scores[1, 0];
            public decimal? Score1_1 => this.Scores[1, 1];
            public decimal? Score1_2 => this.Scores[1, 2];
            public decimal? Score1_3 => this.Scores[1, 3];
            public decimal? Score2_0 => this.Scores[2, 0];
            public decimal? Score2_1 => this.Scores[2, 1];
            public decimal? Score2_2 => this.Scores[2, 2];
            public decimal? Score2_3 => this.Scores[2, 3];
            public decimal? Score3_0 => this.Scores[3, 0];
            public decimal? Score3_1 => this.Scores[3, 1];
            public decimal? Score3_2 => this.Scores[3, 2];
            public decimal? Score3_3 => this.Scores[3, 3];

            public decimal? OtherWin_H { get; set; }
            public decimal? OtherWin_A { get; set; }
            public decimal? OtherDraw { get; set; }
        }


        public IEnumerable<OutcomeProbability> GetScoreProbabilities(Event e, IDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> latestPrices)
        {
            var scores = e.Markets.Select(kvp => kvp.Value).SingleOrDefault(m => m.Type == "CORRECT_SCORE");
            if (scores != null)
            {
                var prices = latestPrices[scores.Id];
                foreach (var outcome in scores.Outcomes)
                {
                    OrderBookObservation outcomePrices;
                    if (prices.TryGetValue(outcome.Id, out outcomePrices))
                    {
                        var count = 0M;
                        var priceSum = 0M;
                        if (outcomePrices.ToBack.Count > 0)
                        {
                            priceSum += outcomePrices.ToBack.OrderByDescending(p => p.Price).First().Price;
                            count += 1;
                        }

                        if (outcomePrices.ToLay.Count > 0)
                        {
                            priceSum += outcomePrices.ToLay.OrderBy(p => p.Price).First().Price;
                            count += 1;
                        }

                        if (count < 1)
                        {
                            continue;
                        }

                        var probability = 1M / (priceSum / count);

                        Score score;
                        if (Score.TryParse(outcome.Name, out score))
                        {
                            yield return new OutcomeProbability
                            {
                                Score = score,
                                Probability = probability
                            };
                        }
                        else
                        {
                            switch (outcome.Name)
                            {
                                case "Any Other Home Win":
                                    yield return new OutcomeProbability { OtherScore = new OtherScore("H"), Probability = probability };
                                    break;
                                case "Any Other Away Win":
                                    yield return new OutcomeProbability { OtherScore = new OtherScore("A"), Probability = probability };
                                    break;
                                case "Any Other Draw":
                                    yield return new OutcomeProbability { OtherScore = new OtherScore("D"), Probability = probability };
                                    break;
                                default:
                                    throw new NotSupportedException($"{outcome.Name} not supported");
                            }
                        }
                    }
                }
            }
        }
    }



    public class OtherScore
    {
        public OtherScore(string winnner)
        {
            Winnner = winnner;
        }

        public string Winnner { get; }

        public override string ToString()
        {
            return this.Winnner;
        }
    }

    public class OutcomeProbability
    {
        public Score Score { get; set; }
        public OtherScore OtherScore { get; set; }
        public decimal Probability { get; set; }

        public override string ToString()
        {
            return $"{Math.Round(this.Probability*100)}% {this.Score} {this.OtherScore}";
        }
    }

    public class Score
    {
        public static bool TryParse(string text, out Score score)
        {
            score = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Split('-').Select(p => p.Trim()).ToArray();
            if(parts.Length != 2)
            {
                return false;
            }

            var home = 0;
            if(!int.TryParse(parts[0], out home))
            {
                return false;
            }

            var away = 0;
            if(!int.TryParse(parts[1], out away))
            {
                return false;
            }

            score = new Score(home, away);

            return true;
        }

        public Score(int home, int away)
        {
            this.Home = home;
            this.Away = away;
        }

        public int Home { get; }
        public int Away { get; }

        public override string ToString()
        {
            return $"{this.Home} - {this.Away}";
        }
    }

    public class OutcomePayout
    {
    }
}


