using Kronikol.NUnit4;
using NUnit.Framework;

// Two classes of one name in two namespaces, for TagMappingTests: NUnit names a test's feature after its class alone. No
// [Test], so NUnit never runs them.
namespace Kronikol.Tests.NUnit4.Web
{
    [Category("Web")]
    internal sealed class Orders : DiagrammedComponentTest
    {
        public void Pay() { }
    }
}

namespace Kronikol.Tests.NUnit4.Api
{
    [Category("Api")]
    internal sealed class Orders : DiagrammedComponentTest
    {
        public void Pay() { }
    }
}
