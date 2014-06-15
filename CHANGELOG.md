Changelog
=========

v1.3.3
------

 - ~/aws-info writter, for scripts
 - Better error reporting
 - Numerous bug fixes

v1.3.2
------

 - Change snapshot prefix from `Ec2Manager - ` to `[Ec2Manager]`

v1.3.1
------

 - Fix a bug preventing you from listing other users' snapshots

v1.3.0
------

 - Better logging for scripts
 - Elastic IPs no longer used
 - 'Friends' system which auto-lists snapshots, instead of using configuration files
 - Add option to delete old snapshot when creating a new one
 - Scripts starting with CWD set sensibly
 - Installer moved form NSIS to InnoSetup
 - Improve dropped connection handling
 - README updates

v1.2.2
------

 - Snapshot creation auto-populates name and description if possible
 - Better handling of connection problems
 - Add prompt to download latest version
 - README updates

v1.2.1
--------

 - Bug fix: Successful commands were sometimes misinterpreted as having error'd
 - Menu item to allow creation of a new empty volume

v1.2.0
------

 - PuTTY key conversion - can launch PuTTY directly
 - Menu bar additions
 - Better responsiveness
 - Script support
 - Snapshot creation

v1.1.1
------

 - Fix bug which caused crash on startup if credentials weren't set

v1.1.0
------

 - Reconnect to running instances
 - Support spot instances
 - Cancel instance/volume creation
 - UI tweaks

v1.0.0
------

 - Initial release
