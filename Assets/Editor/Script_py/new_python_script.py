import os
import sys
import json
from dotenv import load_dotenv
from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI

# Check if we're running from Unity or from the server
if len(sys.argv) > 1:
    # Running from server with parameters file
    params_file = sys.argv[1]
    with open(params_file, 'r') as f:
        params = json.load(f)
    prompt = params.get('prompt', "What's the meaning of life?")
else:
    # Default when running from Unity Editor
    prompt = "What's the meaning of life?"

# Get the project path
project_path = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
script_dir = os.path.dirname(os.path.abspath(__file__))

# Add the script directory to sys.path
if script_dir not in sys.path:
    sys.path.append(script_dir)

# Load environment variables
load_dotenv(os.path.join(script_dir, '.env'))

# Get API keys
api_key = os.environ.get("OPENAI_API_KEY")
langchain_api_key = os.environ.get("LANGCHAIN_API_KEY")

# If keys not found in environment, try to read directly from .env file
if not api_key or not langchain_api_key:
    try:
        with open(os.path.join(script_dir, '.env'), 'r') as f:
            content = f.read()
            for line in content.split('\n'):
                if line.startswith('OPENAI_API_KEY='):
                    api_key = line.strip().split('=', 1)[1].strip('"\'')
                elif line.startswith('LANGCHAIN_API_KEY='):
                    langchain_api_key = line.strip().split('=', 1)[1].strip('"\'')
    except Exception as e:
        print(f"Error reading .env file: {e}")

# Set up LangChain tracing
os.environ["LANGCHAIN_TRACING_V2"] = "true"
os.environ["LANGCHAIN_API_KEY"] = langchain_api_key

# Initialize the LLM
proxy_picker_llm = ChatOpenAI(
    model="gpt-4",
    temperature=0.1,
    base_url="https://reverse.onechats.top/v1",
    api_key=api_key
)

# Create the prompt
prompt_system = SystemMessage(content=prompt)

# Get the response
response = proxy_picker_llm.invoke([prompt_system])

# Print the response (this will be captured by the server)
print(response.content)