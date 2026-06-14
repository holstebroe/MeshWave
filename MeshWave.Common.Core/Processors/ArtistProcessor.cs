using MeshWave.Common.Core.Models;

namespace MeshWave.Common.Core.Processors;

public class ArtistProcessor : BaseCatalogueEntryProcessor
{
    public override string TargetType => CatalogueEntryType.Artist;
}
