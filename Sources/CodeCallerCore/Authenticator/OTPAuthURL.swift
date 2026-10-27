import Foundation

/// Reads `otpauth://totp/...` links, the format inside every authenticator QR code.
public enum OTPAuthURL {
    public static func account(from link: String) -> OTPAccount? {
        let link = link.trimmingCharacters(in: .whitespacesAndNewlines)
        // Some generators leave spaces in labels like "Acme Inc:alice" unencoded.
        guard let components = URLComponents(string: link) ?? URLComponents(string: link.replacingOccurrences(of: " ", with: "%20")),
              components.scheme?.lowercased() == "otpauth",
              components.host?.lowercased() == "totp" else { return nil }
        let items = Dictionary(
            (components.queryItems ?? []).map { ($0.name.lowercased(), $0.value ?? "") },
            uniquingKeysWith: { first, _ in first }
        )
        guard let secret = items["secret"].flatMap(Base32.decode) else { return nil }

        let algorithm: OTPAlgorithm
        if let name = items["algorithm"] {
            // An unknown algorithm would give wrong codes, so the account is skipped instead.
            guard let known = OTPAlgorithm(name: name) else { return nil }
            algorithm = known
        } else {
            algorithm = .sha1
        }

        var label = components.path
        if label.hasPrefix("/") { label.removeFirst() }
        var issuer = (items["issuer"] ?? "").replacingOccurrences(of: "+", with: " ")
        var name = label
        if let colon = label.firstIndex(of: ":") {
            name = String(label[label.index(after: colon)...])
            if issuer.isEmpty { issuer = String(label[..<colon]) }
        }

        let account = OTPAccount(
            issuer: issuer,
            name: name,
            secret: secret,
            algorithm: algorithm,
            digits: items["digits"].flatMap { Int($0) } ?? 6,
            period: items["period"].flatMap { Int($0) } ?? 30
        )
        return account.isValid ? account : nil
    }
}
