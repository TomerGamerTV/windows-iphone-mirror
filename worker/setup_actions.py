"""Explicit, confirmation-gated iPhone setup actions."""

from __future__ import annotations

import asyncio
import contextlib
import re

from connection_windows import get_tunnel


class SetupError(RuntimeError):
    def __init__(self, code: str):
        super().__init__(code)
        self.code = code


MUTATING_ACTIONS = {"pair-usb", "reveal-developer-mode", "prepare-image", "pair-wifi"}


async def select_usb(serial: str | None = None) -> str:
    from pymobiledevice3.exceptions import ConnectionFailedToUsbmuxdError
    from pymobiledevice3.usbmux import list_devices

    try:
        phones = [
            device
            for device in await list_devices()
            if device.is_usb and (not serial or device.matches_udid(serial))
        ]
    except (OSError, ConnectionFailedToUsbmuxdError) as error:
        raise SetupError("apple_device_support_missing") from error
    if not phones:
        raise SetupError("usb_phone_missing")
    if len(phones) != 1:
        raise SetupError("multiple_devices")
    return phones[0].serial


async def list_usb_devices() -> list[dict]:
    from pymobiledevice3.exceptions import ConnectionFailedToUsbmuxdError
    from pymobiledevice3.pair_records import iter_remote_paired_identifiers
    from pymobiledevice3.usbmux import list_devices

    try:
        devices = [device for device in await list_devices() if device.is_usb]
    except (OSError, ConnectionFailedToUsbmuxdError) as error:
        raise SetupError("apple_device_support_missing") from error
    paired = {value.replace("-", "").casefold() for value in iter_remote_paired_identifiers()}
    result = []
    usb_serials = set()
    usb_records = []
    for device in devices:
        serial = str(device.serial)
        normalized = serial.replace("-", "").casefold()
        usb_serials.add(normalized)
        usb_records.append((serial, normalized in paired))
    result.extend(await asyncio.gather(*(_probe_usb_device(serial, wifi_paired) for serial, wifi_paired in usb_records)))
    for identifier in sorted(iter_remote_paired_identifiers()):
        normalized = identifier.replace("-", "").casefold()
        if normalized in usb_serials:
            continue
        result.append(
            {
                "serial": identifier,
                "name": None,
                "transport": "wifi",
                "trusted": True,
                "developer_mode": False,
                "wifi_paired": True,
            }
        )
    return result


async def _probe_usb_device(serial: str, wifi_paired: bool) -> dict:
    """Read status without pairing or changing anything on the phone."""
    from pymobiledevice3.lockdown import create_using_usbmux

    record = {
        "serial": serial,
        "name": None,
        "transport": "usb",
        "trusted": False,
        "developer_mode": False,
        "wifi_paired": wifi_paired,
    }
    client = None
    try:
        client = await asyncio.wait_for(
            create_using_usbmux(serial=serial, autopair=False, connection_type="USB"), 5
        )
        record["trusted"] = True
        try:
            name = await asyncio.wait_for(client.get_value(key="DeviceName"), 3)
            if isinstance(name, str) and name:
                record["name"] = name
        except Exception:
            pass
        try:
            developer_mode = await asyncio.wait_for(client.get_developer_mode_status(), 3)
            record["developer_mode"] = type(developer_mode) is bool and developer_mode
        except Exception as error:
            record["state_error"] = _setup_error_code(error)
    except Exception as error:
        record["state_error"] = _setup_error_code(error)
    finally:
        if client is not None:
            with contextlib.suppress(Exception):
                await asyncio.wait_for(client.close(), 2)
    return record


def _setup_error_code(error: BaseException) -> str:
    return {
        "NotPairedError": "usb_trust_required",
        "NotTrustedError": "usb_trust_required",
        "InvalidHostIDError": "usb_trust_required",
        "UserDeniedPairingError": "trust_declined",
        "PairingDialogResponsePendingError": "trust_pending",
        "DeviceHasPasscodeSetError": "iphone_locked",
        "DeveloperDiskImageNotFoundError": "image_required",
        "DeviceVersionNotSupportedError": "unsupported_usb_version",
    }.get(type(error).__name__, "connection_failed")


async def change_phone(action: str, serial: str) -> None:
    from pymobiledevice3.lockdown import create_using_usbmux

    client = await create_using_usbmux(serial=serial, autopair=False, connection_type="USB")
    try:
        if action == "pair-usb":
            await client.pair()
        elif action == "reveal-developer-mode":
            from pymobiledevice3.services.amfi import AmfiService

            await AmfiService(client).reveal_developer_mode_option_in_ui()
        elif action == "prepare-image":
            from pymobiledevice3.services.mobile_image_mounter import auto_mount

            await auto_mount(client)
        elif action == "pair-wifi":
            from pymobiledevice3.exceptions import RemotePairingCompletedError
            from pymobiledevice3.remote.tunnel_service import RemotePairingLockdownService

            service = await RemotePairingLockdownService.create(client)
            try:
                try:
                    await service.connect(autopair=True)
                except RemotePairingCompletedError:
                    pass
            finally:
                await service.close()
        else:
            raise SetupError("invalid_setup_action")
    finally:
        await client.close()


async def inspect_phone(serial: str) -> dict:
    from pymobiledevice3.lockdown import create_using_usbmux
    from pymobiledevice3.pair_records import iter_remote_paired_identifiers
    from pymobiledevice3.services.mobile_image_mounter import MobileImageMounterService

    client = await create_using_usbmux(serial=serial, autopair=False, connection_type="USB")
    try:
        version = client.product_version
        if not isinstance(version, str) or not re.fullmatch(r"\d+(?:\.\d+){1,2}", version):
            raise SetupError("unknown_ios_version")
        developer_mode = await client.get_developer_mode_status()
        if type(developer_mode) is not bool:
            raise SetupError("unknown_developer_mode")
        async with MobileImageMounterService(client) as service:
            images = await service.copy_devices()
        records = list(iter_remote_paired_identifiers())
        normalize = lambda value: value.replace("-", "").casefold()
        return {
            "ios_version": version,
            "developer_mode": developer_mode,
            "mounted_image_count": len(images),
            "usb_transport_supported": tuple(map(int, version.split(".")[:2])) >= (17, 4),
            "wifi_pairing_saved": any(normalize(item) == normalize(serial) for item in records),
            "saved_wifi_pairing_count": len(records),
            "mirroring_verified": False,
        }
    finally:
        await client.close()


async def display_capabilities(serial: str) -> dict:
    from pymobiledevice3.remote.core_device.display_service import DisplayService

    async with get_tunnel("usb", serial) as rsd:
        if "com.apple.coredevice.displayservice" not in rsd.peer_info.get("Services", {}):
            raise SetupError("display_service_missing")
        async with DisplayService(rsd) as service:
            response = await service.get_media_support_info()
        flags = response.get("supportedFeatures")
        if not isinstance(flags, int) or isinstance(flags, bool) or flags < 0:
            raise SetupError("unknown_display_features")
        if flags == 0:
            raise SetupError("display_features_unavailable")
        return {"supported_media_features": int(flags), "mirroring_verified": False}


async def execute(action: str, serial: str | None, approved: bool) -> dict:
    if action in MUTATING_ACTIONS and not approved:
        raise SetupError("confirmation_required")
    serial = await asyncio.wait_for(select_usb(serial), 10)
    if action in ("pair-usb", "reveal-developer-mode", "pair-wifi"):
        timeout = 90 if action == "pair-wifi" else 45
        await asyncio.wait_for(change_phone(action, serial), timeout)
        return {"action": action, "serial": serial, "completed": True}

    state = await asyncio.wait_for(inspect_phone(serial), 20)
    if action == "check":
        return {"action": action, "serial": serial, "state": state}
    if not state["usb_transport_supported"]:
        raise SetupError("unsupported_usb_version")
    if not state["developer_mode"]:
        raise SetupError("developer_mode_required")
    if action == "prepare-image":
        if state["mounted_image_count"]:
            return {"action": action, "serial": serial, "state": state, "already_mounted": True}
        await asyncio.wait_for(change_phone(action, serial), 300)
        state = await asyncio.wait_for(inspect_phone(serial), 20)
        if not state["mounted_image_count"]:
            raise SetupError("image_not_mounted")
        return {"action": action, "serial": serial, "state": state, "already_mounted": False}
    if action != "check-display":
        raise SetupError("invalid_setup_action")
    if not state["mounted_image_count"]:
        raise SetupError("image_required")
    return {
        "action": action,
        "serial": serial,
        "state": state,
        "display": await asyncio.wait_for(display_capabilities(serial), 30),
    }
