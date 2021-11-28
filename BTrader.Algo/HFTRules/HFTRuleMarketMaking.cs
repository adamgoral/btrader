namespace BTrader.Algo.HFTRules
{

    public class HFTRuleMarketMaking : HFTRule
    {
        public HFTRuleMarketMaking(int lowerSpreadDistance, int upperSpreadDistance)
        {
            LowerSpreadDistance = lowerSpreadDistance;
            UpperSpreadDistance = upperSpreadDistance;
        }

        public int LowerSpreadDistance { get; }
        public int UpperSpreadDistance { get; }

        public override string ToString()
        {
            return $"mm({this.LowerSpreadDistance};{this.UpperSpreadDistance})";
        }
    }

}
