#:project ../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// Writes Stylesheets.VioletThemeStyleSheet, as this build compiles it, to the file named by the argument
// (for s3bytes.py, which needs the old and the new theme text to tell a move from an edit).
File.WriteAllText(args[0], Kronikol.Stylesheets.VioletThemeStyleSheet);
