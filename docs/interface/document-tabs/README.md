# Document tabs

Tabs above the viewport represent open documents with independent sessions. A tab displays its document title and dirty state. Existing close and document-safety behavior is described in [Saving, closing and recovery copies](../document-safety/README.md).

## Context menu

The owner approved the revised scope on 2026-11-05 ([issue #69](https://github.com/chrizpro/prism/issues/69)). Right-click a tab for **Close All**, **Close All But Pinned**, **Pin tab** / **Unpin tab**, **File Name**, **File Path**, and **Open File Path**. Close All includes pinned tabs; Close All But Pinned skips them. Both review each dirty document or unfinished edit with Save, Don't save and Cancel. Cancel stops the operation; documents already closed remain closed. Pinning lasts for the current session. File Name and File Path copy text; Open File Path reveals the saved file on Windows/macOS and opens its containing folder on Linux. File actions are disabled for untitled documents. As with individual tab closing, a fresh empty document is created when the last tab closes.

Pin persistence across restarts remains an open decision. [Headless tests](../../../Prism.Tests/Headless/DocumentTabs/DocumentTabTests.cs) cover pin exclusion and Cancel/Discard; actual desktop menu, clipboard and file-manager behavior remain to be verified.

**Close All But This**, approved by the owner on 2026-11-05, preserves the right-clicked tab and reviews every other tab, including pinned tabs, through the same Save / Don't save / Cancel flow. Cancel stops further closing and keeps the selected tab.

On 2026-11-05, the Windows headless run `dotnet test Prism.Tests/Headless/Prism.HeadlessTests.csproj -c Release --filter 'Feature=Images|Feature=DocumentTabs|Feature=DocumentSafety' --no-restore` passed 16 tests at base `b1d0a23846c821683c41d789b7e9fe52f76addf7` plus uncommitted changes in the shared dirty workspace. This includes one DocumentTabs test; it does not verify the desktop menu.

After the revised Close All behavior, the same filtered run passed 18 tests on 2026-11-05, including two DocumentTabs cases covering pinned inclusion/exclusion, Cancel, Discard, successful Save and canceled Save As. The desktop build passed; the actual context menu was not visually checked.

After approval of Close All But This, the same filtered command passed 19 tests on 2026-11-05, including TAB-H003 for preserving the target and reviewing another pinned document. The run used Windows with base `b1d0a23846c821683c41d789b7e9fe52f76addf7` plus local changes; it did not verify the menu visually.
