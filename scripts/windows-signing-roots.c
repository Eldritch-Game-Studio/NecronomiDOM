/* Restore only the two approved Microsoft-SDK signing roots in an owned prefix.
 * The caller authenticates SDK bundles and enforces a read-only host sandbox.
 * No TLS purpose, leaf trust, signature exception or revocation override is added.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wincrypt.h>
#include <stdio.h>
#include <string.h>

static const char *const fingerprints[] = {
    "3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c",
    "2399561127a57125de8cefea610ddf2fa078b5c8067f4e828290bfb860e84b3c"
};

static int matches_hash(PCCERT_CONTEXT certificate, const char *expected)
{
    BYTE bytes[32];
    DWORD size = sizeof(bytes);
    char actual[65];
    static const char hex[] = "0123456789abcdef";
    if (!CryptHashCertificate2(L"SHA256", 0, NULL, certificate->pbCertEncoded,
                              certificate->cbCertEncoded, bytes, &size)
        || size != sizeof(bytes)) {
        fprintf(stderr, "Certificate SHA256 computation failed: win32=%lu\n", GetLastError());
        return 0;
    }
    for (unsigned i = 0; i < sizeof(bytes); ++i) {
        actual[i * 2] = hex[bytes[i] >> 4];
        actual[i * 2 + 1] = hex[bytes[i] & 15];
    }
    actual[64] = 0;
    return strcmp(actual, expected) == 0;
}

static int signing_purposes_only(PCCERT_CONTEXT certificate)
{
    union { CERT_ENHKEY_USAGE alignment; BYTE bytes[512]; } buffer;
    DWORD size = sizeof(buffer);
    PCERT_ENHKEY_USAGE usage = (PCERT_ENHKEY_USAGE)buffer.bytes;
    unsigned found = 0;
    if (!CertGetEnhancedKeyUsage(certificate, CERT_FIND_PROP_ONLY_ENHKEY_USAGE_FLAG,
                                usage, &size) || usage->cUsageIdentifier != 2) return 0;
    for (DWORD i = 0; i < usage->cUsageIdentifier; ++i) {
        if (strcmp(usage->rgpszUsageIdentifier[i], szOID_PKIX_KP_CODE_SIGNING) == 0) found |= 1;
        else if (strcmp(usage->rgpszUsageIdentifier[i], szOID_PKIX_KP_TIMESTAMP_SIGNING) == 0) found |= 2;
        else return 0;
    }
    return found == 3;
}

static int restore_root(HCERTSTORE store, const char *directory, const char *expected)
{
    char path[1024];
    BYTE encoded[65536];
    FILE *file = NULL;
    PCCERT_CONTEXT certificate = NULL, existing = NULL, installed = NULL;
    int result = 1;
    if (_snprintf_s(path, sizeof(path), _TRUNCATE, "%s\\%s.cer", directory, expected) < 0)
        return 1;
    if (fopen_s(&file, path, "rb") != 0) {
        fprintf(stderr, "Cannot open authenticated root: %s\n", path);
        return 1;
    }
    size_t size = fread(encoded, 1, sizeof(encoded), file);
    int complete = feof(file) && !ferror(file);
    fclose(file);
    if (!complete || size == 0) return 1;
    certificate = CertCreateCertificateContext(X509_ASN_ENCODING, encoded, (DWORD)size);
    if (!certificate || !matches_hash(certificate, expected)) goto finish;
    existing = CertFindCertificateInStore(store, X509_ASN_ENCODING, 0,
                                         CERT_FIND_EXISTING, certificate, NULL);
    printf("SIGNING_ROOT_PRE sha256=%s present=%s\n", expected, existing ? "true" : "false");
    if (existing) {
        /* Never alter a pre-existing root's trust purposes, especially TLS. */
        if (!matches_hash(existing, expected) || !signing_purposes_only(existing)) goto finish;
    } else {
        LPSTR identifiers[] = {szOID_PKIX_KP_CODE_SIGNING, szOID_PKIX_KP_TIMESTAMP_SIGNING};
        CERT_ENHKEY_USAGE usage = {2, identifiers};
        /* Set restrictions before inserting the context into the trusted store. */
        if (!CertSetEnhancedKeyUsage(certificate, &usage)
            || !signing_purposes_only(certificate)
            || !CertAddCertificateContextToStore(store, certificate, CERT_STORE_ADD_NEW, NULL))
            goto finish;
    }
    installed = CertFindCertificateInStore(store, X509_ASN_ENCODING, 0,
                                          CERT_FIND_EXISTING, certificate, NULL);
    if (!installed || !matches_hash(installed, expected) || !signing_purposes_only(installed))
        goto finish;
    printf("SIGNING_ROOT_POST sha256=%s eku=codeSigning,timeStamping tls=false\n", expected);
    result = 0;
finish:
    if (result) fprintf(stderr, "Signing root restoration failed: %s win32=%lu\n", expected, GetLastError());
    if (installed) CertFreeCertificateContext(installed);
    if (existing) CertFreeCertificateContext(existing);
    if (certificate) CertFreeCertificateContext(certificate);
    return result;
}

int main(int argc, char **argv)
{
    if (argc != 2) return 2;
    HCERTSTORE store = CertOpenStore(CERT_STORE_PROV_SYSTEM_A, 0, 0,
        CERT_SYSTEM_STORE_CURRENT_USER, "ROOT");
    if (!store) {
        fprintf(stderr, "Cannot open owned CurrentUser ROOT store: win32=%lu\n", GetLastError());
        return 1;
    }
    int result = 0;
    for (unsigned i = 0; i < sizeof(fingerprints) / sizeof(fingerprints[0]); ++i) {
        if (restore_root(store, argv[1], fingerprints[i])) { result = 1; break; }
    }
    if (!CertCloseStore(store, CERT_CLOSE_STORE_CHECK_FLAG)) result = 1;
    return result;
}
