import Foundation

/// Chooses which codes to offer when you ask for recent ones instead of just the newest.
public enum RecentCodes {
    /// How many codes the recent codes list shows at most.
    public static let limit = 5

    /// The newest `limit` items, newest first, with each code only once. A code that arrived
    /// twice, like one sent by both email and text, keeps its newest copy.
    public static func pick<Item>(
        _ items: [Item],
        limit: Int = limit,
        code: (Item) -> String,
        received: (Item) -> Date
    ) -> [Item] {
        guard limit > 0 else { return [] }
        // Sorting the positions keeps items that arrived at the same moment in their original order.
        let newestFirst = items.indices.sorted { first, second in
            let a = received(items[first]), b = received(items[second])
            return a == b ? first < second : a > b
        }
        var seen: Set<String> = []
        var picked: [Item] = []
        for index in newestFirst where seen.insert(code(items[index])).inserted {
            picked.append(items[index])
            if picked.count == limit { break }
        }
        return picked
    }
}
