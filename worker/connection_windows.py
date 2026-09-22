"""Windows CoreDevice transport selection without Linux route tooling."""

from __future__ import annotations

import asyncio
import contextlib
import ipaddress
import os
import socket
import subprocess

import psutil
from pymobiledevice3.exceptions import ConnectionFailedToUsbmuxdError
from pymobiledevice3.remote import userspace_tunnel as ut
from pymobiledevice3.remote.tunnel_service import (
    RemotePairingTunnelService,
    browse_remotepairing,
    iter_remote_paired_identifiers,
)

MODES = ("usb", "wifi", "auto")


class ConnectionSelectionError(RuntimeError):
    def __init__(self, code: str):
        super().__init__(code)
        self.code = code


async def select_connection(mode: str, serial: str | None = None) -> tuple[str, str | None]:
    if mode not in MODES:
        raise ConnectionSelectionError("invalid_connection_mode")
    if mode == "wifi":
        return "wifi", serial

    from pymobiledevice3.usbmux import list_devices

    try:
        devices = [
            device
            for device in await list_devices()
            if device.is_usb and (not serial or device.matches_udid(serial))
        ]
    except (OSError, ConnectionFailedToUsbmuxdError) as error:
        if mode != "auto":
            raise ConnectionSelectionError("apple_device_support_missing") from error
        devices = []

    if len(devices) > 1:
        raise ConnectionSelectionError("multiple_devices")
    if devices:
        return "usb", devices[0].serial
    if mode == "auto":
        return "wifi", serial
    raise ConnectionSelectionError("usb_phone_missing")


def _interface_for_local_address(local_address: str) -> str | None:
    local = local_address.split("%", 1)[0].lower()
    for interface, addresses in psutil.net_if_addrs().items():
        for address in addresses:
            value = (address.address or "").split("%", 1)[0].lower()
            if value == local:
                return interface
    return None


def _route_source(address: str, port: int) -> str | None:
    try:
        candidates = socket.getaddrinfo(address, port, type=socket.SOCK_DGRAM)
    except OSError:
        return None
    for family, socktype, proto, _, sockaddr in candidates:
        try:
            with socket.socket(family, socktype, proto) as probe:
                probe.connect(sockaddr)
                return probe.getsockname()[0]
        except OSError:
            continue
    return None


def _interface_description(interface: str) -> str:
    """Read the Windows adapter description without exposing a console window."""
    env = os.environ.copy()
    env["IPHONE_MIRROR_INTERFACE"] = interface
    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    try:
        result = subprocess.run(
            [
                "powershell.exe",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "(Get-NetAdapter -Name $env:IPHONE_MIRROR_INTERFACE -ErrorAction SilentlyContinue).InterfaceDescription",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=2,
            check=False,
            env=env,
            creationflags=creationflags,
        )
    except (OSError, subprocess.SubprocessError):
        return ""
    return result.stdout.strip()


async def network_route_allowed(address: str, port: int) -> bool:
    raw_address = address.split("%", 1)[0]
    try:
        remote = ipaddress.ip_address(raw_address)
    except ValueError:
        return False
    if remote.is_loopback or remote.is_unspecified or remote.is_multicast:
        return False

    local_address = await asyncio.to_thread(_route_source, address, port)
    if not local_address:
        return False
    try:
        local = ipaddress.ip_address(local_address.split("%", 1)[0])
    except ValueError:
        return False
    if local.is_loopback or local.is_unspecified:
        return False

    interface = _interface_for_local_address(local_address)
    if not interface:
        return False
    description = await asyncio.to_thread(_interface_description, interface)
    lowered = f"{interface} {description}".casefold()
    blocked_fragments = (
        "tailscale",
        "wireguard",
        "zerotier",
        "vpn",
        "tunnel",
        "tun ",
        "tap ",
        "loopback",
        "apple mobile device ethernet",
        "iphone",
        "remote ndis",
    )
    if any(fragment in lowered for fragment in blocked_fragments):
        return False

    stats = psutil.net_if_stats().get(interface)
    return bool(stats and stats.isup)


async def connect_wifi(identifier: str, address: str, port: int) -> RemotePairingTunnelService:
    service = RemotePairingTunnelService(identifier, address, port)
    try:
        await asyncio.wait_for(service.connect(autopair=False), 8)
        return service
    except BaseException:
        with contextlib.suppress(Exception):
            await asyncio.wait_for(service.close(), 1)
        raise


async def discover_wifi(timeout: float = 4.0) -> list[dict]:
    """Return mDNS remote-pairing endpoints as address/port pairs for the UI."""
    try:
        answers = await browse_remotepairing(timeout=timeout)
    except Exception:
        return []
    endpoints = {
        (address.full_ip, answer.port)
        for answer in answers
        for address in answer.addresses
    }

    def sort_key(endpoint):
        address, port = endpoint
        try:
            parsed = ipaddress.ip_address(address)
            return (parsed.version, int(parsed), port)
        except ValueError:
            return (99, 0, str(address), port)

    ordered = sorted(endpoints, key=sort_key)
    return [{"address": address, "port": int(port)} for address, port in ordered]


async def wifi_provider(serial=None, autopair=False, remotepairing_fallback=False, direct_address=None, direct_port=49152):
    identifiers = list(iter_remote_paired_identifiers())
    if serial:
        normalized = serial.replace("-", "").casefold()
        identifiers = [item for item in identifiers if item.replace("-", "").casefold() == normalized]
    if not identifiers:
        raise ConnectionSelectionError("wifi_pairing_required")
    if len(identifiers) > 1:
        raise ConnectionSelectionError("multiple_devices")

    identifier = identifiers[0]
    if direct_address:
        if not await network_route_allowed(direct_address, direct_port):
            raise ConnectionSelectionError("wifi_unreachable")
        try:
            return await connect_wifi(identifier, direct_address, direct_port), None
        except (OSError, TimeoutError, asyncio.IncompleteReadError):
            raise ConnectionSelectionError("wifi_unreachable")
    answers = await browse_remotepairing(timeout=4)
    endpoints = {
        (address.full_ip, answer.port)
        for answer in answers
        for address in answer.addresses
    }
    endpoints = sorted(endpoints, key=lambda endpoint: (":" in endpoint[0], endpoint[0], endpoint[1]))
    for address, port in endpoints:
        try:
            if not await network_route_allowed(address, port):
                continue
            return await connect_wifi(identifier, address, port), None
        except (OSError, TimeoutError, asyncio.IncompleteReadError):
            continue
    raise ConnectionSelectionError("wifi_unreachable")


class WifiTunnel(ut.UserspaceRsdTunnel):
    def __init__(self, *, wifi_address=None, wifi_port=49152, **kwargs):
        super().__init__(**kwargs)
        self.wifi_address = wifi_address
        self.wifi_port = wifi_port

    async def _aopen_locked(self):
        original = ut._create_no_root_tunnel_provider
        async def direct_wifi_provider(*args, **kwargs):
            return await wifi_provider(*args, **kwargs, direct_address=self.wifi_address, direct_port=self.wifi_port)
        ut._create_no_root_tunnel_provider = direct_wifi_provider
        try:
            return await super()._aopen_locked()
        finally:
            ut._create_no_root_tunnel_provider = original


def get_tunnel(mode: str, serial: str | None, wifi_address=None, wifi_port=49152):
    if mode == "wifi":
        return WifiTunnel(serial=serial, autopair=False, remotepairing_fallback=False, wifi_address=wifi_address, wifi_port=wifi_port)
    return ut.UserspaceRsdTunnel(serial=serial, autopair=False, remotepairing_fallback=False)
