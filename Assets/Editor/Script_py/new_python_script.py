import os
import sys
import UnityEngine

try:
    # Get the project path from Unity
    project_path = UnityEngine.Application.dataPath
    script_dir = os.path.join(project_path, "Editor", "Script_py")

    # Add the script directory to sys.path
    if script_dir not in sys.path:
        sys.path.append(script_dir)

    # Import required modules
    from dotenv import load_dotenv
    import httpx
    from langchain_core.messages import HumanMessage, SystemMessage
    from langchain_openai import ChatOpenAI

    # Path to the .env file
    env_path = os.path.join(script_dir, '.env')

    if os.path.exists(env_path):
        load_dotenv(env_path)
    else:
        # Try alternative paths
        alt_paths = [
            os.path.join(project_path, ".env"),
            os.path.join(os.path.dirname(project_path), ".env"),
            os.path.join(os.getcwd(), ".env")
        ]

        for path in alt_paths:
            if os.path.exists(path):
                env_path = path
                load_dotenv(env_path)
                break

    # Get API keys
    api_key = os.environ.get("OPENAI_API_KEY")
    langchain_api_key = os.environ.get("LANGCHAIN_API_KEY")

    # If keys not found in environment, try to read directly from .env file
    if not api_key or not langchain_api_key:
        if os.path.exists(env_path):
            try:
                with open(env_path, 'r') as f:
                    content = f.read()

                    for line in content.splitlines():
                        if line.startswith('OPENAI_API_KEY=') and not api_key:
                            api_key = line.strip().split('=', 1)[1].strip('"\'')
                            os.environ["OPENAI_API_KEY"] = api_key

                        if line.startswith('LANGCHAIN_API_KEY=') and not langchain_api_key:
                            langchain_api_key = line.strip().split('=', 1)[1].strip('"\'')
                            os.environ["LANGCHAIN_API_KEY"] = langchain_api_key
            except Exception as e:
                UnityEngine.Debug.LogError(f"Failed to read .env file: {e}")

    if api_key and langchain_api_key:
        # Set up LangChain tracing
        os.environ["LANGCHAIN_TRACING_V2"] = "true"
        os.environ["LANGCHAIN_PROJECT"] = "proxy_picker"

        # Initialize the LLM
        proxy_picker_llm = ChatOpenAI(
            model="gpt-4",
            temperature=0.1,
            base_url="https://reverse.onechats.top/v1",
            api_key=api_key
        )

        # Create the prompt
        prompt_system = SystemMessage(content="""What's the meaning of life?""")

        # Get the response
        response = proxy_picker_llm.invoke([prompt_system])

        # Log the response to Unity console
        response_text = str(response)
        UnityEngine.Debug.Log(f"LLM Response: {response_text}")
    else:
        missing_keys = []
        if not api_key:
            missing_keys.append("OPENAI_API_KEY")
        if not langchain_api_key:
            missing_keys.append("LANGCHAIN_API_KEY")

        UnityEngine.Debug.LogError(f"Missing required API keys: {', '.join(missing_keys)}")

except Exception as e:
    UnityEngine.Debug.LogError(f"Error in Python script: {str(e)}")
    import traceback

    UnityEngine.Debug.LogError(traceback.format_exc())