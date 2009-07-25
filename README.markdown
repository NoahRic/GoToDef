An editor extension for Visual Studio 2010 (Beta1) that allows the user to hold down the control key + click to invoke the *Go To Definition* command.

All code is released under the [Ms-PL license](http://www.opensource.org/licenses/ms-pl.html).

To download the extension, go to [GoToDef extension on the VS Gallery](http://visualstudiogallery.msdn.microsoft.com/en-us/4b286b9c-4dd5-416b-b143-e31d36dc622b).

I wrote a blog article describing how the extension is put together; you can read it [here](http://blogs.msdn.com/noahric/archive/2009/07/05/time-spent-in-design.aspx).

The version numbers on the VS extension gallery (up to v0.2) do *not* correlate with any specific versions in this git repo.

Here is the description from the extension itself:
Make ctrl+click perform a "Go To Definition" on the identifier under the cursor. Also, when the control key is held down, highlight identifiers under the mouse that look like they have definitions to navigate to.

The extension is based, in part, on an earlier sample written by Huizhong Long, a QA member of the editor team (thanks Huizhong!).

* v1.1 - Mouse cursor turns to a hand when over a link.  Also, a non-functional change - remove accidentally included assemblies (the add reference dialog added them as CopyLocal=true, which bloated the vsix size up to about 800kb, from about 40kb).
