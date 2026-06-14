using System.Collections.Generic;

namespace MeshWave.Common.Core.Processors;

public static class CatalogueProcessorDefaults
{
    public static IEnumerable<ICatalogueEntryProcessor> GetDefaultProcessors()
    {
        return new ICatalogueEntryProcessor[]
        {
            new ArtistProcessor(),
            new AlbumProcessor(),
            new TrackProcessor(),
            new PlaylistProcessor()
        };
    }
}
