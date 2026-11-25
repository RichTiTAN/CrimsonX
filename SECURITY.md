# Security policy

## Reporting a vulnerability

Email **CrimsonVPN.Support@proton.me** with:

- a description of the issue and its impact
- the CrimsonX version and your OS
- steps to reproduce, or a proof of concept

Please do not open a public issue for a vulnerability, and give me a reasonable
window to fix it before publishing details.

## Scope

In scope: the CrimsonX application and its own code, the install scripts in
`install/`, and the update mechanism.

Out of scope: the bundled third-party engines (Xray, sing-box, amnezia-box,
aether, crimsonipsec) and the proxy or VPN servers you connect to. Report those
to their upstream projects:

- https://github.com/XTLS/Xray-core
- https://github.com/SagerNet/sing-box
- https://github.com/amnezia-vpn/amnezia-box
- https://github.com/CluvexStudio/Aether
- https://github.com/n0madic/go-ipsec

## Supported versions

Only the latest release is supported. Versions up to and including 3.3.3 were
released under the GPLv3 and are no longer maintained.
