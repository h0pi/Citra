# Demo company: Northwind Outdoor

A fictional company for Citra's demo (the same one as the demo website in `/demo-site`). All names, prices and
rules are made up.

## The company

- **Northwind Outdoor d.o.o.**, an online shop for outdoor gear: tents, backpacks, sleeping bags, boots and
  camping accessories. Founded in 2012 in Mostar, Bosnia and Herzegovina; about 40 employees.
- Sells online across Europe and in one showroom in Mostar. Prices in EUR.
- Own brand "Northwind" plus a few partner brands.

## Two bots

| Bot | Who asks | Documents |
|---|---|---|
| **Customer support** (public, on the website) | Shop customers | Shipping, returns, warranty, payment, price list, product manuals, size guides, FAQ, privacy policy |
| **HR assistant** (internal) | Employees | Employee handbook, leave and sick-leave rules, travel expenses, remote work, onboarding, IT and security rules |

## Files

- `source/support/*.md`, `source/hr/*.md` — the text of every document (simple markup, easy to edit).
- `documents/support/`, `documents/hr/` — the generated PDF and Word files that the demo bots answer from.
- `generate.py` — rebuilds `documents/` from `source/`:

```bash
python -m pip install reportlab python-docx
python generate.py
```

## Documents (C-252, C-253)

Customer support:
1. Shipping policy (zones, prices, delivery times — a table)
2. Returns and refunds policy
3. Warranty policy (by product type — a table)
4. Payment methods and invoices
5. Price list 2026 (3-page PDF with tables)
6. Tent manuals: Ridgeline 2 and Basecamp 4 (2-page PDFs with spec tables)
7. Backpack manual and fitting guide
8. Sleeping bag guide (temperature ratings — a table)
9. Boot size guide (EU/UK/US sizes — a table)
10. Care and repair guide
11. Customer FAQ
12. Privacy policy
13. Loyalty programme rules
14. Store and showroom information (address, opening hours)

HR assistant:
15. Employee handbook (3-page PDF)
16. Annual leave and public holidays
17. Sick leave and parental leave
18. Travel and expense policy (allowances — a table)
19. Remote and hybrid work policy
20. Onboarding checklist for new employees
21. IT and security rules
22. Employee discount and benefits
23. Code of conduct
24. Performance reviews and salary bands (a table)

## Test questions (C-254)

`eval/support-questions.csv` (22 questions) and `eval/hr-questions.csv` (18 questions): each with its expected
answer and the document the answer comes from. They import straight into an eval set (Evaluation → Import CSV;
columns `question`, `expected_answer`, `expected_document`). `eval/make_questions.py` writes them.

## Seeding the demo (C-255)

With the API and the Worker running, `seed.py` creates the whole demo through Citra's normal API (standard
Python, no extra packages):

```bash
python seed.py --api http://localhost:5100 --email owner@northwind-outdoor.example --password <a new password>
```

It signs up the company "Northwind Outdoor" (that email becomes its owner), creates the two bots with their
settings (Customer support is public for the website widget), uploads all documents and creates one eval set per
bot from the CSV files. The Worker then reads the documents in the background; on Voyage's free tier this takes
about 10 minutes. Running it again stops with "already exists" instead of creating a second copy.

## Eval results (C-259)

Baseline with the free stand-in answer model (it copies matching sentences) and the free word judge
(2026-11-10):

| Bot | Score | Correct / partly / wrong | Right document found |
|---|---|---|---|
| Customer support | 68.2% | 11 / 8 / 3 | 22 of 22 |
| HR assistant | 77.8% | 11 / 6 / 1 | 18 of 18 |

Search finds the right document for every question. The answers fall short only because the stand-in quotes
sentences or table rows instead of answering, and the word judge counts correct answers in other words as
partly correct. Tuning towards 90% is planned once a real answer model is connected.

