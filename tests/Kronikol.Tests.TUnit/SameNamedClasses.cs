using Kronikol.TUnit;
using CategoryAttribute = TUnit.Core.CategoryAttribute;

// Two classes of one name in two namespaces, for TagMappingTests: the adapter names a test's feature after its class
// alone. TUnit does not run this project.
namespace Kronikol.Tests.TUnit.Web
{
    [Category("Web")]
    [Category("Shop")]
    internal sealed class Orders : DiagrammedComponentTest;
}

namespace Kronikol.Tests.TUnit.Api
{
    [Category("Api")]
    [Category("Shop")]
    internal sealed class Orders : DiagrammedComponentTest;
}
