using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MeshWave.LibraryManager
{
    /// <summary>
    /// Manages the community music folder (read-only for user, updated by synchronizer).
    /// </summary>
    public class CommunityLibraryManager
    {
        private readonly string _communityPath;
        public CommunityLibraryManager(string communityPath)
        {
            _communityPath = communityPath;
        }

        public IEnumerable<string> GetAllCommunityTracks()
        {
            // TODO: Implement retrieval of community tracks (read-only)
            return Array.Empty<string>();
        }

        public void SyncFromNetwork()
        {
            // TODO: Implement sync logic (called by synchronizer)
        }
    }
}
