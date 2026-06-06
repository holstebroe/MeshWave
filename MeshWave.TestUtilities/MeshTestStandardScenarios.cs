using System;
using System.Collections.Generic;
using System.Text;

namespace MeshWave.TestUtilities
{
    public class MeshTestStandardScenarios
    {
        public static async Task<MeshTestContext> CreateSingleUserScenario()
        {
            var context = new MeshTestContext();
            // Setup single user scenario
            var john = await context.CreatePeerAsync("John");
            return context;
        }
    }
}
