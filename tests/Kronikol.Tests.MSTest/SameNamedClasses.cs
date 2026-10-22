using Microsoft.VisualStudio.TestTools.UnitTesting;

// Two classes of one name in two namespaces, for TagMappingTests: the adapter names a test's feature after its class
// alone. No [TestClass], so MSTest never runs them.
namespace Kronikol.Tests.MSTest.Web
{
    [TestCategory("Web")]
    internal sealed class Orders
    {
        public void Pay() { }
    }
}

namespace Kronikol.Tests.MSTest.Api
{
    [TestCategory("Api")]
    internal sealed class Orders
    {
        public void Refund() { }
    }
}
