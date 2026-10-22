using Kronikol.xUnit2;
using Xunit;

// Two test classes of one name in two namespaces, for TagMappingTests: the adapter names a test's feature after its class
// alone. No [Fact], so xUnit never runs them.
namespace Kronikol.Tests.xUnit2.Web
{
    [TestTracking]
    [Trait("Category", "Web")]
    [Trait("Category", "Shop")]
    public class Orders
    {
        public void Pay() { }
    }
}

namespace Kronikol.Tests.xUnit2.Api
{
    [TestTracking]
    [Trait("Category", "Api")]
    [Trait("Category", "Shop")]
    public class Orders
    {
        public void Refund() { }
    }
}
