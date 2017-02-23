using System;

namespace G2AChecker
{
    class Game
    {
        private decimal _price;
        private decimal _minimalPrice;

        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price
        {
            get { return _price * Rate; }
            set { _price = value; }
        }
        public decimal MinPrice
        {
            get { return _minimalPrice * Rate; }
            set { _minimalPrice = value; }
        }
        public DateTime MinPriceDate { get; set; }
        public DateTime LastTimeUpdated { get; set; }
        public string Url { get; set; }
        public decimal Rate { get; set; }
    }
}
