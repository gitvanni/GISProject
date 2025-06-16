namespace GISProject.Models
{
    public class DijkstraResult
    {
        public int Seq { get; set; }
        public int Node { get; set; }
        public int Edge { get; set; }
        public double Cost { get; set; }
        public double? AggCost { get; set; }
    }
}
