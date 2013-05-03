Ec2Manager
==========

**Important! This tool uses your Amazon AWS account to creates (and destroy) EC2 instances and EBS volumes.
While they are cheap, if you leave something running and forget about it, it is going to cost you.
I am not responsible for any money you spend using this tool.**

Make sure you keep on eye on the [Ec2 Console](https://console.aws.amazon.com/ec2/home?region=eu-west-1) (File -> Ec2 Console).

Introduction
------------

Ec2Manager was written to solve a specific problem: occasionally I'd want to get a group of friends together and play a multiplayer game online.
We wanted a private dedicated server, but no-one's internet connection was good enough to host one.

Amazon's [Ec2](http://aws.amazon.com/ec2/) service has potential here - you can start up a server in a matter of minutes, and terminate it when you're done.
Ec2Manager was written to allow you to set up a server in only a couple of clicks, and load up a pre-configured dedicated server without having to start from scratch.

Installing
----------

You're welcome to build the project from source.
You'll need Visual Studio 2012 to build the project, and [NSIS](http://nsis.sourceforge.net) if you want to build the installer.

Alternatively, you can grab the [latest installer](http://canton7-ec2manager.s3.amazonaws.com/Releases/Ec2Manager-latest.exe) or a [standalone .zip](http://canton7-ec2manager.s3.amazonaws.com/Releases/Ec2Manager-latest.zip).

How does it work?
-----------------

A bit of background knowledge is essential here, as you'll probably get very confused quite quickly without it.

When you click `Create Instance`, Ec2Manager creates a new security group (defines your firewall rules), a key pair (allows you to log into the instance you created), a public IP for your instance, and creates a new instance using them.
Once that's started (takes a few minutes), it establishes an SSH connection using the key pair.

Now, you can create new virtual hard disks (called EBS volumes - Elastic Block Store) of any size, and attach them to your instance.
By default they're unformatted, but once you're put some stuff on one, you can create a read-only snapshot (which is stored in Amazon's S3 storage service, cheaply), which you can use as the basis for new EBS volumes.

I've created, and maintain, a number of snapshots for different game servers.
When you click `Mount Volume`, Ec2Manager creates a new EBS volume based off the snapshot you selected.
It attaches it to your instance, then uses its SSH connection to mount the volume.

You can then start the game server stored on the volume.

Quick Start
-----------

First off, you'll need an Amazon AWS account.
[Create one here](https://portal.aws.amazon.com/gp/aws/developer/registration/index.html) if you haven't already.

Next, start the application.
If this is your first time starting it, you'll be prompted to input your AWS credentials (or you can change these by going to File -> Settings).
Stick them in, and you're ready to go.

Next, select an instance size.
The default is a Micro instance, which falls into Amazon's [free tier](http://aws.amazon.com/free/).
See also the [comparison of instance sizes](http://aws.amazon.com/ec2/instance-types/).

When you're ready to launch this instance, click `Create Instance`.
This process will take a few minutes, and when it's done you'll be in charge of a running server.

Once the instance has started, you've got a number of options open to you.
You can terminate the instance, which will gracefully shut it down and remove all associated resources.
It is very important that you terminate the instance when you've finished with it, or it will cost you money.

What you'll want to do now, however, is mount a new volume.
If you read the 'How does it work' section above, you'll know that volumes are essentialy new hard disks, created from a snapshot.
The snapshot can contain almost anything, and you're free to create your own (covered later on).
For now, however, select one of the snapshots I maintain, and click `Mount Volume`.

This will take a small amount of time.
When it's mounted, necessary packages will be installed, ports opened, and the default command to run loaded.
Customise this to your needs, then click `Launch` to run it.

Feel free to mount more volumes and run more commands.

SSHing into your instance
-------------------------

Once you've created and connected to an instance, you have the option of saving the private key used to log into the instance.
This is in OpenSSH format, so you can use it directly with OpenSSH clients.
If you want to use it with [PuTTY](http://the.earth.li/~sgtatham/putty/latest/x86/putty.exe), you'll need to convert this key to PuTTY's format using [PuTTYgen](http://the.earth.li/~sgtatham/putty/latest/x86/puttygen.exe).
Go to Conversions -> Import Key, and browse to the key you saved from Ec2Manager.
Then click `Save Private Key`, click `Yes`, and save this somewhere.

Next, fire up PuTTY, and in the Host Name box put `username@public-ip`, where `username` is from the `Login as` box in Ec2Manager, and `public-ip` is from the `IP` field in the header of instance's tab in Ec2Manager, for example `ubuntu@123.456.789.012`.
Go to Connection -> SSH -> Auth, and browse to the key you saved a second ago in the `Private key file for authentication` box.
Click open and you're in!

Creating new Snapshots, and incorporating into Ec2Manager
---------------------------------------------------------

New snapshots can be created either from scratch (say you want to create a new game server), or from an existing snapshot (say you want to customise someone else's snapshot).
I've found two slightly different approaches fit these two scenarios best, so I'll approach them separately.

### Creating a snapshot from scratch

The best approach here is to fire up a new instance, and build the contents of the volume on the instance itself.
When it's finished (and most importantly you know the size, as volumes aren't resizable) you can move that over to a volume.

So, fire up a new instance.
You can use Ec2Manager for that (launch a new volume but don't mount anything), or launch it with Ec2 Console.
SSH in, and create a new folder.
Install whatever you need to install, and get it working, tweaking the firewall rules in Ec2 Console - Security Group as appropriate.
If you're creating a snapshot for a new game, please check below to make sure the ports it's using don't clash with any other server.

When you're done (ish), check the size of your folder (`du -sch .` from just inside the folder is a big help), then create a new volume of corresponding size.
Attach it (I suggest not using /dev/sdf or /dev/sdg, in case you want to mount a volume from Ec2Manager to compare ec2manager-specific configuration files), then mount it using e.g. (assuming you attached the volume as `/dev/sdg` or `/dev/xvdg`) `sudo mkfs.ext4 /dev/xvdg; mkdir xvdg; sudo mount /dev/xvdg xvdg; sudo chown ubuntu.ubuntu xvdg`.
Move over your files, and create the Ec2Manager-specific configuration files (see below).

When you're done, detach the volume, terminate the instance, and spin up a new instance in Ec2Manager.
Load a custom snapshot or volume, and specify the volume you just created to test it.
When you're sure it works (SSH in and fix things if it doesn't), terminate the instance and create a new snapshot from the volume in Ec2 Console.
You're done!

### Creating a new snapshot from an existing snapshot

Here it's definitely worth firing up an instance with Ec2Manager, and mount the volume you want to copy.
When that's done, SSH in and tweak the volune to your needs.
Before terminating the instance, create a new snaphshot using Ec2 Console.
When you're done, terminate the instance as normal.

### Incorporating new volumes and instances into Ec2Manager

Ec2Manager's drop-down list of volumes is built from two places: the official list (hosted by me) and your personal list.
The location of your personal list depends on how you installed Ec2Manager.

If you grabbed a standalone zip, there should be a 'config' folder in the same directory as Ec2Manager.
In there, create a file called `snapshot-config.txt`, and copy the format from [the official list](http://canton7-ec2manager.s3.amazonaws.com/snapshot-config.txt) (that is, `snapshot-or-volume-id[space]Description`).

Alternatively, host a snapshot-config.txt somewhere, and point the appropriate key in Ec2Manager.exe.config to it.

Ec2Manager-specific configuration files
---------------------------------------

Each volume has a number of configuration files used by Ec2Manager, including what firewall ports to open, what other packages need installing, the suggested way to start the server, and any instructions to the users.
Let's detail them...

1. `ec2manager/setup`: This is an executable file (don't forget the shebang!) which is executed once the volume has been mounted.
Use it to install any necessary packages, etc.
2. `ec2manager/ports`: This text file contains the ports which needs to be opened.
The format is one port of range per line, of the format `fromport-toport/protocol`, e.g. `1000-2000/tcp`.
If you only want to open one port, that's allowed -- e.g. `1000/tcp` -- and if you want to open both TCP and UDP, skip that bit -- e.g. `2000` or `2000-2010`.
3. `ec2manager/runcmd`: This text file has a single line, which is the suggested command used to start the server.
This is run from the root of the mounted volume.
4. `ec2manager/user_instruction`: This is displayed to the user, verbatim. The string `<PUBLIC-IP>` is replaced with the actual public IP of the server.

Port Mappings
-------------

To try and avoid multiple game servers from using the same ports, this is a list of the different ports in use.
If you're creating a new snapshot, please respect this.

 - Left 4 dead 2: 
 - Hidden Source Beta 4b:
 - Teamspeak:
 - Killing Floor:

Gratitude
---------

Thanks for [Adam Whitcroft](http://thenounproject.com/adamwhitcroft) at [The Noun Project](http://thenounproject.com) for the icon!
