"""Check TLS and SMTP authentication only; never send an email.

Set AIVES_SMTP_USERNAME and AIVES_SMTP_PASSWORD from private configuration.
Optional: AIVES_SMTP_HOST (smtp.gmail.com), AIVES_SMTP_PORT (587).
"""
import os
import ssl
import smtplib
import sys

try:
    with smtplib.SMTP(os.environ.get("AIVES_SMTP_HOST", "smtp.gmail.com"), int(os.environ.get("AIVES_SMTP_PORT", "587")), timeout=20) as smtp:
        smtp.ehlo()
        smtp.starttls(context=ssl.create_default_context())
        smtp.ehlo()
        code, _ = smtp.login(os.environ["AIVES_SMTP_USERNAME"], os.environ["AIVES_SMTP_PASSWORD"])
        print(f"SMTP STARTTLS and authentication: PASS ({code}); no message sent")
except smtplib.SMTPResponseException as error:
    print(f"SMTP authentication: FAIL ({error.smtp_code}); no message sent")
    sys.exit(1)
except Exception as error:
    print(f"SMTP connectivity: FAIL ({type(error).__name__}); no message sent")
    sys.exit(1)
