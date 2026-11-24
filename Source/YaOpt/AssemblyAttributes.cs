using System.Runtime.CompilerServices;

// Publicizer only rewrites the copy of the assembly the compiler sees. At runtime the game
// loads the original Assembly-CSharp / 0Harmony, so access checks still run. The Unsafe
// strategy relies on SkipVerification, which only ever worked on older Mono; CoreCLR honours
// IgnoresAccessChecksTo instead.
//
// Publicizer would emit these two lines itself through the AssemblyAttribute mechanism, but
// this project sets GenerateAssemblyInfo to false, which disables that mechanism entirely.
// The attribute type comes from the Krafs.Publicizer content file, so keep both names in sync
// with the <Publicize> items in YaOpt.csproj.
// Lib.Harmony carries its own copy of this attribute, and publicizing 0Harmony imports it.
// The compiler resolves to the local definition, which is what we want, so silence the clash.
#pragma warning disable CS0436

[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]
[assembly: IgnoresAccessChecksTo("0Harmony")]

#pragma warning restore CS0436
