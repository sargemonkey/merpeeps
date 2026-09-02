using System.Runtime.CompilerServices;

// The headless UI tests drive the real window, so they need the two narrow
// hooks it exposes internally. Kept to one assembly rather than making the
// window's internals public.
[assembly: InternalsVisibleTo("Merpeeps.App.Tests")]
