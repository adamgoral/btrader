namespace BTrader.Domain
{
    public struct PriceSize
    {
        public PriceSize(decimal price, decimal size)
        {
            this.Price = price;
            this.Size = size;
        }

        public decimal Price { get; }
        public decimal Size { get; }

        public override string ToString()
        {
            return $"{this.Size}@{this.Price}";
        }
    }
}