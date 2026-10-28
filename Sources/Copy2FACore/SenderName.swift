import Foundation

public enum SenderName {
    /// A short, readable name for a sender such as `"GitHub" <noreply@github.com>`.
    public static func display(_ sender: String) -> String {
        let trimmed = sender.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let open = trimmed.firstIndex(of: "<") else {
            return trimmed.contains("@") ? domain(of: trimmed) : trimmed
        }
        let name = trimmed[..<open].trimmingCharacters(in: CharacterSet.whitespaces.union(CharacterSet(charactersIn: "\"'")))
        if !name.isEmpty { return name }
        let address = trimmed[trimmed.index(after: open)...].prefix { $0 != ">" }
        return domain(of: String(address))
    }

    static func domain(of address: String) -> String {
        guard let at = address.lastIndex(of: "@") else { return address }
        let labels = address[address.index(after: at)...].lowercased().split(separator: ".")
        guard labels.count > 2 else { return labels.joined(separator: ".") }
        // Shorten "accounts.google.com" to "google.com", but keep "example.co.uk" whole.
        let isCountryPair = labels[labels.count - 2].count <= 3 && labels[labels.count - 1].count == 2
        return labels.suffix(isCountryPair ? 3 : 2).joined(separator: ".")
    }
}
