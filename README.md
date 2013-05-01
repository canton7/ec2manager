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

Amazong's [Ec2](http://aws.amazon.com/ec2/) service has potential here - you can start up a server in a matter of minutes, and terminate it when you're done.
Ec2Manager was written to allow you to set up a server in only a couple of clicks, and load up a pre-configured dedicated server without having to start from scratch.

Installing
----------

You're welcome to build the project from source.
You'll need Visual Studio 2012 to build the project, and [NSIS](http://nsis.sourceforge.net) if you want to build the installer.

Alternatively, you can grab the latest installer from TODO or a standalone .zip from TODO.

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

This will take a small amount of time,


SSHing into your instance
-------------------------

Creating new Snapshots
----------------------

Scrapbook
---------

The snapshot contains some configuration data for Ec2Manager, including what firewall ports to open, and what other packages need installer.
Ec2Manager dutifully follows these instructions, reads back the suggested command to start the server, and a user instruction, then passes control back to you.


