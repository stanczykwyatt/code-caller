import Testing
@testable import Copy2FACore

@Suite("Sender names")
struct SenderNames {
    @Test func usesTheDisplayName() {
        #expect(SenderName.display("GitHub <noreply@github.com>") == "GitHub")
        #expect(SenderName.display("\"Apple\" <appleid@id.apple.com>") == "Apple")
    }

    @Test func fallsBackToTheDomain() {
        #expect(SenderName.display("<no-reply@accounts.google.com>") == "google.com")
        #expect(SenderName.display("noreply@github.com") == "github.com")
    }

    @Test func keepsCountryDomainsWhole() {
        #expect(SenderName.display("alerts@mail.example.co.uk") == "example.co.uk")
    }
}
