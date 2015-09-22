using System;

namespace G2AChecker
{
	class Game
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public decimal Price { get; set; }
		public decimal MinPrice { get; set; }
		public DateTime MinPriceDate { get; set; }
		public string Url { get; set; }
	}
}
