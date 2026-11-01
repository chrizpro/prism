# Panel action bars

Every panel must keep its action bar visible at the bottom of its available height. Place terminal actions (Done, Finish, Apply, Bind, Cancel) in a fixed Grid row or a bottom-docked footer, outside the body ScrollViewer. The panel host must provide finite height and must not wrap the whole panel in another ScrollViewer.

Only settings and supporting content scroll. Contextual actions alongside individual fields can remain in the body. Panels without a commit step do not need an artificial footer. Check short panel heights and both scroll extremes when changing a panel layout.
