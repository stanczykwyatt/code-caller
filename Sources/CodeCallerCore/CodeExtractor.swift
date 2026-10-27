import Foundation

/// Finds one-time verification codes in emails and text messages.
///
/// A message has to read like a sign-in or verification message before any code
/// is accepted. Every number or code-shaped word in it is then scored by its
/// shape and by the words around it, and the best one wins if it scores high
/// enough.
public enum CodeExtractor {
    /// The score a candidate needs before it counts as a code.
    static let threshold = 6

    /// Returns the most likely verification code in an email, or nil if it doesn't contain one.
    public static func code(subject: String, body: String) -> String? {
        let subject = normalize(subject)
        let body = normalize(String(body.prefix(20_000)))
        guard isVerificationMessage(subject: subject, body: body) else { return nil }
        return bestCode(in: [(subject, true), (body, false)])
    }

    /// Returns the most likely verification code in a text message, or nil if it doesn't contain one.
    ///
    /// Texts have no subject and are short, so a word like "code" or "PIN" is enough to
    /// consider one. Promotions such as "use code SAVE20 at checkout" are still skipped.
    public static func code(inText text: String) -> String? {
        let text = normalize(String(text.prefix(2_000)))
        guard contains(Patterns.verification, in: text) || contains(Patterns.codeWord, in: text) else { return nil }
        return bestCode(in: [(text, false)])
    }

    private static func bestCode(in texts: [(String, Bool)]) -> String? {
        var best: (code: String, score: Int)?
        for (text, inSubject) in texts {
            let text = text as NSString
            let context = Context(
                text: text,
                inSubject: inSubject,
                keywords: ranges(of: Patterns.keyword, in: text),
                unrelated: ranges(of: Patterns.unrelated, in: text),
                promotions: ranges(of: Patterns.promotion, in: text),
                dates: ranges(of: Patterns.date, in: text)
            )
            for candidate in candidates(in: text) {
                let score = score(candidate, in: context)
                if score >= threshold, score > (best?.score ?? .min) {
                    best = (candidate.code, score)
                }
            }
        }
        return best?.code
    }

    // MARK: - Candidates

    struct Candidate {
        enum Shape { case digits, groupedDigits, hyphenated, mixed }

        let range: NSRange
        let code: String
        let shape: Shape

        var shapeScore: Int {
            switch shape {
            case .digits: code.count == 6 ? 4 : 3
            case .groupedDigits: 4
            case .hyphenated: 3
            case .mixed: 2
            }
        }

        var couldBeYear: Bool {
            shape == .digits && code.count == 4 && (code.hasPrefix("19") || code.hasPrefix("20"))
        }
    }

    static func candidates(in text: NSString) -> [Candidate] {
        var found: [Candidate] = []
        func add(_ pattern: NSRegularExpression, _ shape: Candidate.Shape, code: (String) -> String = { $0 }) {
            for match in pattern.matches(in: text as String, range: NSRange(location: 0, length: text.length)) {
                let range = match.range
                guard !found.contains(where: { NSIntersectionRange($0.range, range).length > 0 }),
                      !isPartOfLargerValue(range, in: text) else { continue }
                found.append(Candidate(range: range, code: code(text.substring(with: range)), shape: shape))
            }
        }
        // Grouped digits go first so "482 913" isn't also read as two shorter numbers.
        add(Patterns.groupedDigits, .groupedDigits) { $0.filter(\.isNumber) }
        add(Patterns.digits, .digits)
        add(Patterns.hyphenated, .hyphenated)
        add(Patterns.mixed, .mixed)
        return found
    }

    /// Prices, percentages, phone numbers, dates and order numbers have telltale
    /// characters right next to the digits.
    static func isPartOfLargerValue(_ range: NSRange, in text: NSString) -> Bool {
        func character(at index: Int) -> unichar? {
            index >= 0 && index < text.length ? text.character(at: index) : nil
        }
        let start = range.location
        let end = NSMaxRange(range)
        if let before = character(at: start - 1) {
            if Characters.valueMarks.contains(before) { return true }
            if Characters.joiners.contains(before), let other = character(at: start - 2), Characters.isDigit(other) {
                return true
            }
        }
        if let after = character(at: end) {
            if after == Characters.percent { return true }
            if Characters.joiners.contains(after), let other = character(at: end + 1), Characters.isDigit(other) {
                return true
            }
        }
        return false
    }

    // MARK: - Scoring

    struct Context {
        let text: NSString
        let inSubject: Bool
        let keywords: [NSRange]
        let unrelated: [NSRange]
        let promotions: [NSRange]
        let dates: [NSRange]
    }

    static func score(_ candidate: Candidate, in context: Context) -> Int {
        var score = candidate.shapeScore

        let keywordBefore = distance(before: candidate.range, to: context.keywords)
        let keywordAfter = distance(after: candidate.range, to: context.keywords)
        score += max(bonus(forKeywordBefore: keywordBefore), bonus(forKeywordAfter: keywordAfter))

        if context.inSubject { score += 2 }
        if isOnItsOwnLine(candidate.range, in: context.text) { score += 4 }

        // An order number or a total sits closer to its own label than to the word "code".
        if let distance = distance(before: candidate.range, to: context.unrelated),
           distance <= 40, distance < (keywordBefore ?? .max) {
            score -= 4
        }
        // Promo codes are followed by what they're for, like "at checkout" or "20% off".
        if let distance = distance(after: candidate.range, to: context.promotions), distance <= 25 {
            score -= 4
        }
        if candidate.couldBeYear, let distance = distance(before: candidate.range, to: context.dates), distance <= 20 {
            score -= 5
        }
        return score
    }

    static func bonus(forKeywordBefore distance: Int?) -> Int {
        guard let distance else { return 0 }
        switch distance {
        case ...15: return 5
        case ...40: return 4
        case ...100: return 2
        default: return 0
        }
    }

    /// Covers subjects like "482913 is your Instagram code".
    static func bonus(forKeywordAfter distance: Int?) -> Int {
        guard let distance, distance <= 25 else { return 0 }
        return 3
    }

    static func isOnItsOwnLine(_ range: NSRange, in text: NSString) -> Bool {
        let line = text.substring(with: text.lineRange(for: range))
        return line.trimmingCharacters(in: Characters.lineTrim) == text.substring(with: range)
    }

    static func distance(before range: NSRange, to others: [NSRange]) -> Int? {
        others.filter { NSMaxRange($0) <= range.location }.map { range.location - NSMaxRange($0) }.min()
    }

    static func distance(after range: NSRange, to others: [NSRange]) -> Int? {
        others.filter { $0.location >= NSMaxRange(range) }.map { $0.location - NSMaxRange(range) }.min()
    }

    // MARK: - Text

    static func isVerificationMessage(subject: String, body: String) -> Bool {
        contains(Patterns.verification, in: subject)
            || contains(Patterns.verification, in: body)
            || contains(Patterns.codeWord, in: subject)
    }

    /// Folds full-width digits and letters to ASCII, and removes links and email
    /// addresses, whose numbers are never the code.
    static func normalize(_ text: String) -> String {
        var scalars = String.UnicodeScalarView()
        for scalar in text.unicodeScalars {
            switch scalar.value {
            case 0xFF10...0xFF19, 0xFF21...0xFF3A, 0xFF41...0xFF5A:
                scalars.append(Unicode.Scalar(scalar.value - 0xFEE0) ?? scalar)
            case 0x00A0, 0x2007, 0x202F:
                scalars.append(" ")
            default:
                scalars.append(scalar)
            }
        }
        let folded = String(scalars)
        let whole = NSRange(folded.startIndex..., in: folded)
        return Patterns.link.stringByReplacingMatches(in: folded, range: whole, withTemplate: " ")
    }

    static func ranges(of pattern: NSRegularExpression, in text: NSString) -> [NSRange] {
        pattern.matches(in: text as String, range: NSRange(location: 0, length: text.length)).map(\.range)
    }

    static func contains(_ pattern: NSRegularExpression, in text: String) -> Bool {
        pattern.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) != nil
    }
}

private enum Characters {
    static let valueMarks = Set("$€£¥₹#+№".utf16)
    static let joiners = Set(" -.,:/".utf16)
    static let percent = "%".utf16.first!
    static let lineTrim = CharacterSet.whitespacesAndNewlines.union(CharacterSet(charactersIn: ".:*-–—|>\"'"))

    static func isDigit(_ character: unichar) -> Bool {
        (48...57).contains(character)
    }
}

private enum Patterns {
    /// Latin-script words need letter boundaries so "code" doesn't match "barcode".
    static func words(_ alternatives: String) -> String {
        "(?<!\\p{L})(?:\(alternatives))(?!\\p{L})"
    }

    static let codeWords = words("codes?|passcodes?|otp|pin|c[oó]digos?|codice|kod|код") + "|码|碼|コード|번호"

    static let verificationWords = words(
        "v[eé]rif\\p{L}*|authenticat\\p{L}*|one[- ]?time|otp|passcodes?|2fa|mfa|two[- ]?(?:factor|step)"
            + "|sign(?:ing)?[- ]?in|log(?:ging)?[- ]?in|login|security codes?|access codes?|confirmation codes?"
            + "|launch codes?|magic codes?|connexion|best[äa]tig\\p{L}*|sicherheitscode|anmeld\\p{L}*|einmal\\p{L}*"
            + "|inlog\\p{L}*|accesso|acceso|iniciar sesi[oó]n|подтвержд\\p{L}*|вход\\p{L}*|код\\p{L}*"
    ) + "|验证码|驗證碼|校验码|校驗碼|动态码|動態碼|动态密码|确认码|確認碼|登录|登入|登錄|認証|ワンタイム|ログイン|인증|로그인"

    static let codeWord = regex(codeWords)
    static let verification = regex(verificationWords)
    static let keyword = regex(
        codeWords + "|" + verificationWords + "|"
            + words("passwords?|passwort|kennwort|contrase[ñn]a|mot de passe|security")
    )
    static let unrelated = regex(words(
        "orders?|invoice|receipt|tracking|shipment|ticket|case|promo(?:tion)?|coupon|discount|voucher|gift"
            + "|phone|tel|call|fax|zip|postal|booking|reservation|flight|ref(?:erence)?|ending in|card"
            + "|account number|amount|total|price|pedido|commande|bestellung|factura|rechnung"
    ))
    static let promotion = regex(words(
        "at checkout|checkout|shop now|on sale|sale|deals?|coupon|discount|voucher|promo(?:tion)?|off your"
    ) + "|[0-9]+ ?% off")
    static let date = regex(words(
        "jan|feb|mar|apr|may|jun|jul|aug|sept?|oct|nov|dec|january|february|march|april|june|july"
            + "|august|september|october|november|december|copyright"
    ) + "|©|\\(c\\)")
    static let link = regex("(?:https?://|www\\.)\\S+|[\\w.+-]+@[\\w-]+(?:\\.[\\w-]+)+")

    static let groupedDigits = regex(
        "(?<![0-9A-Za-z])(?:[0-9]{3}[ -][0-9]{3}|[0-9]{4}[ -][0-9]{4})(?![0-9A-Za-z])", caseSensitive: true
    )
    static let digits = regex("(?<![0-9A-Za-z])[0-9]{4,8}(?![0-9A-Za-z])", caseSensitive: true)
    static let hyphenated = regex(
        "(?<![0-9A-Za-z-])(?=[0-9-]*[A-Z])[A-Z0-9]{3,4}-[A-Z0-9]{3,4}(?![0-9A-Za-z-])", caseSensitive: true
    )
    static let mixed = regex(
        "(?<![0-9A-Za-z])(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{5,8}(?![0-9A-Za-z])", caseSensitive: true
    )

    static func regex(_ pattern: String, caseSensitive: Bool = false) -> NSRegularExpression {
        // The patterns are fixed at compile time, so a failure here is a programming error.
        try! NSRegularExpression(pattern: pattern, options: caseSensitive ? [] : [.caseInsensitive])
    }
}
