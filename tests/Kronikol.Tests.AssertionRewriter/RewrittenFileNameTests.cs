using Kronikol.AssertionRewriter;

namespace Kronikol.Tests.AssertionRewriter;

/// <summary>
/// The rewritten copy of a source file has the same name in every build. Until 4.7.3 its prefix was the path's
/// <c>string.GetHashCode</c>, which .NET randomises per process, so each <c>dotnet build</c> wrote every rewritten file
/// under a new name and left the previous build's copy beside it in <c>obj/</c>. The names below are SHA-256 of the path,
/// pinned: a hash that changes from one process to the next cannot match them.
/// </summary>
public class RewrittenFileNameTests
{
    [Fact]
    public void A_file_is_named_for_a_hash_of_its_path_that_is_the_same_in_every_process()
    {
        Assert.Equal("3e0f970d_OrderTests.cs", RewrittenFileNames.Of("/src/Tests/OrderTests.cs"));
    }

    [Fact]
    public void Two_files_of_one_name_in_different_folders_do_not_collide()
    {
        Assert.Equal("b94e306f_OrderTests.cs", RewrittenFileNames.Of("/src/Other/OrderTests.cs"));
    }
}
