using System.Runtime.CompilerServices;

// Expose internal members (the worker middleware and its scope state) to the test project.
[assembly: InternalsVisibleTo("PPDO.Tests")]
