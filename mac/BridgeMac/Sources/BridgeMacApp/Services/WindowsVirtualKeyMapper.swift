import ApplicationServices
import Foundation

enum WindowsVirtualKeyMapper {
    private static let keyMap: [CGKeyCode: UInt16] = [
        0: 0x41,   // A
        1: 0x53,   // S
        2: 0x44,   // D
        3: 0x46,   // F
        4: 0x48,   // H
        5: 0x47,   // G
        6: 0x5A,   // Z
        7: 0x58,   // X
        8: 0x43,   // C
        9: 0x56,   // V
        11: 0x42,  // B
        12: 0x51,  // Q
        13: 0x57,  // W
        14: 0x45,  // E
        15: 0x52,  // R
        16: 0x59,  // Y
        17: 0x54,  // T
        18: 0x31,  // 1
        19: 0x32,  // 2
        20: 0x33,  // 3
        21: 0x34,  // 4
        22: 0x36,  // 6
        23: 0x35,  // 5
        24: 0xBB,  // =
        25: 0x39,  // 9
        26: 0x37,  // 7
        27: 0xBD,  // -
        28: 0x38,  // 8
        29: 0x30,  // 0
        30: 0xDD,  // ]
        31: 0x4F,  // O
        32: 0x55,  // U
        33: 0xDB,  // [
        34: 0x49,  // I
        35: 0x50,  // P
        36: 0x0D,  // Return
        37: 0x4C,  // L
        38: 0x4A,  // J
        39: 0xDE,  // '
        40: 0x4B,  // K
        41: 0xBA,  // ;
        42: 0xDC,  // \
        43: 0xBC,  // ,
        44: 0xBF,  // /
        45: 0x4E,  // N
        46: 0x4D,  // M
        47: 0xBE,  // .
        48: 0x09,  // Tab
        49: 0x20,  // Space
        50: 0xC0,  // `
        51: 0x08,  // Backspace
        53: 0x1B,  // Escape
        57: 0x14,  // Caps Lock
        96: 0x74,  // F5
        97: 0x75,  // F6
        98: 0x76,  // F7
        99: 0x72,  // F3
        100: 0x77, // F8
        101: 0x78, // F9
        103: 0x7A, // F11
        109: 0x79, // F10
        111: 0x7B, // F12
        114: 0x2D, // Insert
        115: 0x24, // Home
        116: 0x21, // Page Up
        117: 0x2E, // Delete
        118: 0x73, // F4
        119: 0x23, // End
        120: 0x71, // F2
        121: 0x22, // Page Down
        122: 0x70, // F1
        123: 0x25, // Left
        124: 0x27, // Right
        125: 0x28, // Down
        126: 0x26  // Up
    ]

    private static let modifierMap: [CGKeyCode: UInt16] = [
        54: 0x5C, // Right Command -> Right Windows
        55: 0x5B, // Left Command -> Left Windows
        56: 0xA0, // Left Shift
        57: 0x14, // Caps Lock
        58: 0xA4, // Left Option -> Left Alt
        59: 0xA2, // Left Control
        60: 0xA1, // Right Shift
        61: 0xA5, // Right Option -> Right Alt
        62: 0xA3  // Right Control
    ]

    static func virtualKey(for keyCode: CGKeyCode) -> UInt16? {
        keyMap[keyCode] ?? modifierMap[keyCode]
    }

    static func modifierVirtualKey(for keyCode: CGKeyCode) -> UInt16? {
        modifierMap[keyCode]
    }

    static func modifierIsDown(for keyCode: CGKeyCode, flags: CGEventFlags) -> Bool? {
        switch keyCode {
        case 54, 55:
            return flags.contains(.maskCommand)
        case 56, 60:
            return flags.contains(.maskShift)
        case 58, 61:
            return flags.contains(.maskAlternate)
        case 59, 62:
            return flags.contains(.maskControl)
        case 57:
            return flags.contains(.maskAlphaShift)
        default:
            return nil
        }
    }
}
