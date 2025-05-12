namespace GISProject.Models
{
    using NetTopologySuite.Geometries;

    public abstract class GeoEntity
    {
        public long Id { get; set; }

        /// <summary>
        /// Geometria associata: può essere Point, Polygon, LineString, ecc.
        /// </summary>
        public Geometry Geometry { get; set; }

        /// <summary>
        /// Data di creazione dell'entità
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Descrizione generica (opzionale)
        /// </summary>
        public string? Description { get; set; }
    }

}
