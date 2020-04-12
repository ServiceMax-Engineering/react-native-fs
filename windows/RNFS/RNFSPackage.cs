using Microsoft.ReactNative.Managed;
using Microsoft.ReactNative;

namespace RNFS
{
    public sealed class RNFSPackage : IReactPackageProvider
    {
        public void CreatePackage(IReactPackageBuilder packageBuilder)
        {
            packageBuilder.AddAttributedModules();
        }
    }
}
