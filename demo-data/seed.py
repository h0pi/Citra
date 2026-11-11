"""Seeds the Northwind Outdoor demo into a running Citra API, using only its public HTTP API.

    python seed.py --api http://localhost:5100 --email owner@northwind-outdoor.example --password <password>

Signs up the demo company (the email becomes its owner), creates the two bots with their settings, uploads every
document from documents/<bot>/ and creates one eval set per bot from eval/<bot>-questions.csv. The Worker then
reads the documents in the background. Stops if the company already exists (log in to check it).
Standard library only.
"""
import argparse
import json
import mimetypes
import sys
import urllib.error
import urllib.request
import uuid
from pathlib import Path

HERE = Path(__file__).parent

BOTS = {
    'support': {
        'name': 'Customer support',
        'description': 'Answers shop customers on the Northwind Outdoor website.',
        'systemPrompt': 'You are the friendly customer support of Northwind Outdoor, an online outdoor-gear shop. '
                        'Answer briefly and mention prices, days and limits exactly as the documents state them.',
        'welcomeMessage': 'Hi! I can help with shipping, returns, warranty, sizes and our products.',
        'widgetColor': '#2F4F3A',
        'starterQuestions': ['How much does shipping cost?', 'How do I return a product?',
                             'Which sleeping bag is warm enough for 0 °C?'],
        'isPublic': True,
        'eval': 'Customer support questions',
    },
    'hr': {
        'name': 'HR assistant',
        'description': 'Answers Northwind Outdoor employees about leave, travel, benefits and company rules.',
        'systemPrompt': 'You help Northwind Outdoor employees with HR questions. Answer briefly and name the '
                        'policy the answer comes from.',
        'welcomeMessage': 'Hi! Ask me about leave, travel expenses, benefits or company rules.',
        'widgetColor': '#6366F1',
        'starterQuestions': ['How many days of annual leave do I get?', 'Can I work from home?',
                             'What is the daily allowance for business trips?'],
        'isPublic': False,
        'eval': 'HR questions',
    },
}


class Api:
    def __init__(self, base):
        self.base = base.rstrip('/')
        self.token = None

    def call(self, method, path, body=None, content_type='application/json'):
        data = json.dumps(body).encode() if content_type == 'application/json' and body is not None else body
        request = urllib.request.Request(self.base + path, data=data, method=method)
        if data is not None:
            request.add_header('Content-Type', content_type)
        if self.token:
            request.add_header('Authorization', f'Bearer {self.token}')
        try:
            with urllib.request.urlopen(request) as response:
                text = response.read().decode()
                return json.loads(text) if text else None
        except urllib.error.HTTPError as error:
            raise SystemExit(f'{method} {path} failed with {error.code}: {error.read().decode()[:500]}')

    def upload(self, path, file):
        boundary = uuid.uuid4().hex
        content_type = mimetypes.guess_type(file.name)[0] or 'application/octet-stream'
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{file.name}"\r\n'
                f'Content-Type: {content_type}\r\n\r\n').encode() + file.read_bytes() + f'\r\n--{boundary}--\r\n'.encode()
        return self.call('POST', path, body, f'multipart/form-data; boundary={boundary}')


def main():
    parser = argparse.ArgumentParser(description='Seed the Northwind Outdoor demo into Citra.')
    parser.add_argument('--api', default='http://localhost:5100')
    parser.add_argument('--email', required=True, help='Owner of the demo company (a new account).')
    parser.add_argument('--password', required=True)
    args = parser.parse_args()
    api = Api(args.api)

    try:
        session = api.call('POST', '/api/auth/register', {
            'companyName': 'Northwind Outdoor', 'displayName': 'Northwind Admin',
            'email': args.email, 'password': args.password,
        })
    except SystemExit as error:
        sys.exit(f'Could not create the demo company (does it exist already?)\n{error}')
    api.token = session['accessToken']

    for folder, settings in BOTS.items():
        bot = api.call('POST', '/api/bots', {'name': settings['name'], 'description': settings['description']})
        api.call('PUT', f"/api/bots/{bot['id']}", {
            'name': settings['name'], 'description': settings['description'],
            'systemPrompt': settings['systemPrompt'], 'welcomeMessage': settings['welcomeMessage'],
            'model': None, 'widgetColor': settings['widgetColor'],
            'starterQuestions': settings['starterQuestions'], 'isPublic': settings['isPublic'],
            'allowedDomains': [], 'dailyMessageLimit': 500,
        })

        documents = sorted((HERE / 'documents' / folder).iterdir())
        for file in documents:
            api.upload(f"/api/bots/{bot['id']}/documents", file)

        questions = (HERE / 'eval' / f'{folder}-questions.csv').read_text(encoding='utf-8')
        eval_set = api.call('POST', f"/api/bots/{bot['id']}/eval-sets",
                            {'name': settings['eval'], 'description': 'Demo test questions with expected answers.'})
        imported = api.call('POST', f"/api/eval-sets/{eval_set['id']}/questions/import", {'csv': questions})
        print(f"{settings['name']}: {len(documents)} documents uploaded, {len(imported['questions'])} test questions")

    print(f"Done. Log in as {args.email}; the Worker is now reading the documents.")


if __name__ == '__main__':
    main()
