"""Writes the demo eval sets as CSV files that import straight into Citra (Evaluation → Import CSV).

    python make_questions.py

Each question names the document its answer comes from, so a run also checks that search finds it.
"""
import csv
from pathlib import Path

HERE = Path(__file__).parent

SUPPORT = [
    ('How much does standard shipping cost within Bosnia and Herzegovina?', 'Standard shipping costs EUR 4.90 within Bosnia and Herzegovina.', 'shipping-policy.pdf'),
    ('From what order value is shipping free to the European Union?', 'Standard shipping to the European Union is free for orders over EUR 120.', 'shipping-policy.pdf'),
    ('When does my order leave the warehouse if I order in the morning?', 'Orders placed before 14:00 on a working day leave the warehouse the same day.', 'shipping-policy.pdf'),
    ('Do you ship to the United Kingdom, and is express delivery available there?', 'Yes, the United Kingdom is in zone 3 with delivery in 5–8 working days, but express delivery is not available there.', 'shipping-policy.pdf'),
    ('How many days do I have to return a product?', 'You can return a product within 30 days of delivery, unused and in its original packaging.', 'returns-and-refunds.docx'),
    ('How much does a return from Germany cost?', 'For returns from outside Bosnia and Herzegovina, EUR 5.90 is deducted from the refund for the return label.', 'returns-and-refunds.docx'),
    ('How long does it take to get my money back after a return?', 'The refund is made within 7 days after the return arrives at the warehouse, to the original payment method.', 'returns-and-refunds.docx'),
    ('Can I return a gas cartridge?', 'No, gas cartridges and fuel bottles cannot be returned, for safety reasons.', 'returns-and-refunds.docx'),
    ('How long is the warranty on a Northwind backpack?', 'Northwind backpacks have a 10-year warranty on fabric, seams, buckles and zippers.', 'warranty-policy.pdf'),
    ('How long is the warranty on Northwind tent poles?', 'Northwind tents have a 5-year warranty on poles, frames and seams.', 'warranty-policy.pdf'),
    ('Can I pay cash on delivery for an order to Croatia?', 'No, cash on delivery is only available within Bosnia and Herzegovina, for orders up to EUR 500, with a fee of EUR 2.', 'payment-and-invoices.docx'),
    ('How long do you hold an order paid by bank transfer?', 'An order paid by bank transfer is held for 5 working days, then cancelled if the payment has not arrived.', 'payment-and-invoices.docx'),
    ('How much does the Basecamp 4 tent cost?', 'The Basecamp 4 costs EUR 449.', 'price-list-2026.pdf'),
    ('What is the article number of the Summit boot?', 'The Summit boot has the article number NW-F-301.', 'price-list-2026.pdf'),
    ('How much does a zipper replacement in the workshop cost?', 'A zipper replacement in the workshop costs from EUR 25; repairs under warranty are free.', 'price-list-2026.pdf'),
    ('What is the comfort temperature of the Ember 0 sleeping bag?', 'The Ember 0 has a comfort temperature of 0 °C and a limit of -6 °C.', 'sleeping-bag-guide.pdf'),
    ('Which EU boot size fits a foot length of 26.7 cm?', 'A foot length of 26.7 cm is EU size 42 (UK 8).', 'boot-size-guide.pdf'),
    ('How tall is the Basecamp 4 inside?', 'The Basecamp 4 is 190 cm high, so you can stand inside.', 'tent-basecamp-4-manual.pdf'),
    ('How much does the Ridgeline 2 weigh?', 'The Ridgeline 2 weighs 2.1 kg in total.', 'tent-ridgeline-2-manual.pdf'),
    ('Which torso lengths does the Trailpack 45 fit?', 'The Trailpack 45 fits torso lengths from 40 to 52 cm.', 'backpack-trailpack-45-manual.pdf'),
    ('How many loyalty points do I need for a 5 euro voucher?', '100 points give a voucher of EUR 5; you earn 1 point per euro and points expire after 24 months.', 'loyalty-programme.docx'),
    ('When is the showroom open on Saturday?', 'On Saturday the showroom is open from 9:00 to 15:00.', 'showroom.pdf'),
]

HR = [
    ('How many days of annual leave do I get?', 'Full-time employees get 25 working days of paid annual leave per year, plus 1 day after every 5 years, up to 30 days.', 'annual-leave.pdf'),
    ('How far in advance must I request leave?', 'Leave must be requested in the HR portal Peak at least 2 weeks in advance, or 4 weeks for leave longer than 10 working days.', 'annual-leave.pdf'),
    ('Can I carry unused leave days over to next year?', 'Up to 5 unused days can be carried over and must be used by 31 March.', 'annual-leave.pdf'),
    ('From which day of sick leave do I need a doctor\'s note?', 'A doctor\'s note is needed from the 3rd day of sick leave.', 'sick-and-parental-leave.pdf'),
    ('How many days of paternity leave do fathers get?', 'Fathers get 10 working days of paid paternity leave, to be taken within 3 months of the birth.', 'sick-and-parental-leave.pdf'),
    ('What is the daily allowance for a business trip to Germany?', 'The daily allowance for other EU countries is EUR 60, with a hotel limit of EUR 130 per night.', 'travel-and-expenses.pdf'),
    ('How much do I get per kilometre when I use my own car?', 'You are paid EUR 0.30 per kilometre.', 'travel-and-expenses.pdf'),
    ('How many days per week can I work from home?', 'Office and support employees can work from home up to 2 days per week after the probation period.', 'remote-work-policy.pdf'),
    ('Is there money for a home office chair or desk?', 'Yes, a one-time allowance of EUR 200 for a chair, desk or screen.', 'remote-work-policy.pdf'),
    ('How long must passwords be?', 'Passwords must be at least 12 characters long, and multi-factor authentication is required.', 'it-security-rules.pdf'),
    ('What should I do with a suspicious email?', 'Do not click links or open attachments and report it to security@northwind-outdoor.example within 1 hour.', 'it-security-rules.pdf'),
    ('What discount do employees get on Northwind products?', 'Employees get 30% off Northwind brand products and 15% off partner brands.', 'benefits-and-discounts.docx'),
    ('How much is the yearly gear allowance?', 'Every employee gets EUR 300 per year for Northwind products.', 'benefits-and-discounts.docx'),
    ('Can I accept a gift from a supplier?', 'Gifts worth up to EUR 50 may be accepted; more valuable gifts must be reported to your manager.', 'code-of-conduct.docx'),
    ('When are performance reviews held?', 'Performance reviews are held twice a year, in March and in September.', 'performance-reviews.pdf'),
    ('What is the salary range for level L2?', 'Level L2 is from BAM 1,800 to BAM 2,500 gross per month.', 'performance-reviews.pdf'),
    ('When is the salary paid?', 'Salaries are paid on the 10th of every month for the previous month.', 'employee-handbook.pdf'),
    ('How long is the probation period?', 'The probation period is 3 months, with a notice period of 7 days during probation.', 'employee-handbook.pdf'),
]


def write(name, rows):
    with open(HERE / name, 'w', encoding='utf-8', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['question', 'expected_answer', 'expected_document'])
        writer.writerows(rows)
    print(f'{name}: {len(rows)} questions')


write('support-questions.csv', SUPPORT)
write('hr-questions.csv', HR)
