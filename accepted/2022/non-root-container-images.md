# Proposal for .NET Non-Root Container Images

We all know that [running as root is considered harmful](https://unix.stackexchange.com/questions/1052/concern-about-logging-in-as-root-overrated) yet it is commonplace to [run container images that way](https://www.redhat.com/blog/understanding-root-inside-and-outside-container). We can make it easier to run .NET container images as non-root.

## Goals

The following will improve security for users:

- Enable all .NET images to be run as a non-root user.
- Exposed ports (via `ASPNETCORE_URLS`) are constant across all image types.
- Images do not use privileged resources by default.
- It is easy to use any combination of root and non-root images in a deployment.

## Plan

We can satisfy those goals (not necessarily in that order) across multiple releases.

- .NET 7
  - Add same non-root user in both rootful and non-root images
  - Expose same non-root ports -- `8080` and `8443` (for HTTPS) -- in both rootful and non-root images
  - Continue to expose port `80` in rootful images, for compatibility.
  - Transition `mcr.microsoft.com/dotnet/samples` to non-root images (which by definition means not exposing port `80`).
- .NET 8
  - Remove port `80` from rootful images.
  - Announce that all .NET images are turnkey non-root capable.

Notes:

- Actual port numbers are TBD.
- Some environments [allow access to privileged ports to non-root users](
https://github.com/dotnet/dotnet-docker/issues/3796).

The rest of this document provides detailed technical context if that's helpful, but is otherwise unnecessary to read.

## Privileged ports

[Ports under `1024` are privileged (require `root` permission)](https://www.w3.org/Daemon/User/Installation/PrivilegedPorts.html), while ports `>= 1024` can be accessed by a regular user.

Kestrel (ASP.NET Core web server) is configured to [listen on port `80`](https://github.com/dotnet/dotnet-docker/blob/7cf01d82858fcc3824574fb92580c4151954699a/src/runtime-deps/6.0/jammy/amd64/Dockerfile#L19) in .NET team provided containers. As a result, we have a significant `root` dependency that we need to address.

In contrast, Red Hat [configures images in OpenShift with `8080` and `8443`](https://github.com/dotnet/aspnetcore/issues/43149#issuecomment-1209525031). That's quite self-descriptive and works great for `non-root` scenarios.

It turns out we already use port `8080` for our [non-root Mariner images](https://github.com/dotnet/dotnet-docker/blob/7cf01d82858fcc3824574fb92580c4151954699a/src/runtime-deps/6.0/cbl-mariner2.0-distroless/amd64/Dockerfile#L52-L53). 

Note: Mariner images are public, but only supported for Microsoft internal usage.

For .NET 7, we should publish images the following way (replacing the existing `ASPNETCORE_URLS` definition):

- Non-root images: `ASPNETCORE_URLS=http://+:8080;https://+:8443`
- Rootful images: `ASPNETCORE_URLS=http://+:80;http://+:8080;https://+:8443`

That approach will enable users to move easily between root and non-root images provided that they adopt the `8xxx` ports. It will also enable TLS usage without needing to re-specify this `ENV`.

At a later point -- hopefully .NET 8 -- we should remove port `80` from the rootful images. The document demonstrates why removing port `80` enables us to significantly improve adoption of non-root containers by our "user" users.

Alternatively, we could adopt ports `5000` and `5001`. ASP.NET Core already uses those as the defaults for development. It's a tradeoff between ports that are more self-descriptive and ports that are more idiomatic for ASP.NET Core.

## .NET Container Hosting Infrastructure

There may infrastructure that exclusively hosts or tests .NET containers and that is hard-coded to port `80`. If those services only support rootful container images, that may be OK. However, it's still not a great model. Instead, infrastructure should configure everything about port publishing and exposure itself to ensure reliability.

For example, imagine a service that was solely using the [`aspnetapp` sample](https://mcr.microsoft.com/product/dotnet/samples/about). We may transition that container image to non-root purely as a technology demonstration. Anyone relying on Kestrel listening on port `80` within the image and by extension being rootful would be broken.

Instead, such a service should redefine the `ASPNETCORE_URLS` ENV to a known non-privileged port of their choosing, like in the following example.

```bash
% docker run --rm -p 8080:8080 -d -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dotnet/samples:aspnetapp 
5130b894c5a2b8c7bb59e2583eb6efe8adb21e8908d97a75e4fae01fce8a038e
rich@MacBook-Air-2 aspnetapp % curl http://localhost:8080/Environment
{"runtimeVersion":".NET 6.0.6","osVersion":"Linux 5.10.104-linuxkit #1 SMP PREEMPT Thu Mar 17 17:05:54 UTC 2022","osArchitecture":"Arm64","processorCount":4,"totalAvailableMemoryBytes":4108652544,"memoryLimit":0,"memoryUsage":0}%
```

The matching ports guarantees that the app will work. Note that the `8080:8080` host:guest port mapping don't need to match on both side. It's the the guest and `ENV` ports that need to match.

The following works equally well.

```bash
rich@MacBook-Air-2 aspnetapp % docker run --rm -p 8088:8080 -d -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dotnet/samples:aspnetapp
44bbd26fa870cfa654f0652d188ea2ac7015231d2e86c27b6747a30bd73c2f89
rich@MacBook-Air-2 aspnetapp % curl http://localhost:8088/Environment
{"runtimeVersion":".NET 6.0.6","osVersion":"Linux 5.10.104-linuxkit #1 SMP PREEMPT Thu Mar 17 17:05:54 UTC 2022","osArchitecture":"Arm64","processorCount":4,"totalAvailableMemoryBytes":4108652544,"memoryLimit":0,"memoryUsage":0}% 
```

## Running images as non-root

You can run .NET images as non-root today, but it isn't straightforward. We can improve that.

There are a spectrum of images we could publish:

- Root
- Root + optional non-root user
- non-root

We almost exclusively publish rootful images today. We may publish non-root images in the future. In the meantime, we can consider publishing root images with an optional non-root user. That would make it very easy to run .NET images as non-root.

The `docker` CLI enables specifying a user with `docker run`. Let's try that.

```bash
$ docker run --rm -it -p 8088:80 -u app mcr.microsoft.com/dotnet/samples:aspnetapp
docker: Error response from daemon: unable to find user app: no matching entries in passwd file.
```

That's not useful and makes sense. We cannot use a user that hasn't been added to the container image.

We can [add one](https://gist.github.com/richlander/34e514446afece01252f19c2a18c3222), however.

```dockerfile
FROM mcr.microsoft.com/dotnet/samples:aspnetapp

RUN groupadd \
        --system \
        --gid=101 \
        app \
    && adduser \
        --uid 101 \
        --gid 101 \
        --shell /bin/false \
        --no-create-home \
        --system \
        app
```

Now, lets try that, both with and w/o a user specified.

```bash
$ docker run --rm -it -p 8088:80 aspnetappwithuser

      Now listening on: http://[::]:80
```

That works as per normal.

Now, let's run as our user.

```bash
$ docker run --rm -it -p 8088:80 -u app aspnetappwithuser

Unhandled exception. System.Net.Sockets.SocketException (13): Permission denied
```

Excellent. It fails. Now let's re-configure the port.

```bash
$ docker run --rm -it -p 8088:8080 -u app -e ASPNETCORE_URLS=http://+:8080 aspnetappwithuser

      Now listening on: http://[::]:8080
```

Works like a dream.

We can also ask the container which account it is working under.

```bash
rich@kamloops:~/aspnetapp$ docker run --rm -d -p 8088:8080 -u app -e ASPNETCORE_URLS=http://+:8080 aspnetappwithuser
cf0f115399059600fd933697dcb64cbf255e459b8a0e2d2b44b53aa71f2029de
rich@kamloops:~/aspnetapp$ docker exec cf0f115399059600fd933697dcb64cbf255e459b8a0e2d2b44b53aa71f2029de whoami
app
```

This little scenario also demonstrates why removing port `80` from the rootful images would be useful. We wouldn't have to redefine  `ASPNETCORE_URLS` in order to switch between `root` and `app` for images that are rootful by default. That would be very nice.

For clarity, the approach that was used to add the user to the `aspnetapp` image was just a proof-of-concept. The intent is to add this user in the `runtime-deps` images (or `runtime` for Windows).
