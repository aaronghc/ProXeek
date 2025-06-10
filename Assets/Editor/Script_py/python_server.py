from flask import Flask, request, jsonify
import subprocess
import os
import sys
import json
import traceback
import threading
import queue

app = Flask(__name__)

# Increase maximum content length to handle multiple images
app.config['MAX_CONTENT_LENGTH'] = 100 * 1024 * 1024  # Allow up to 100MB payloads

# Path to your Unity project's Python scripts
SCRIPTS_PATH = r"C:\Users\aaron\Documents\GitHub\ProXeek\Assets\Editor\Script_py"


@app.route('/run_python', methods=['POST'])
def run_python():
    try:
        print("Received request of size:", len(request.data))
        data = request.json

        if not data:
            print("No JSON data received")
            return jsonify({'status': 'error', 'output': 'No JSON data received'}), 400

        print("Parsed JSON data keys:", data.keys())

        if 'action' not in data or data['action'] != 'run_script':
            print("Invalid request format - missing 'action' or not 'run_script'")
            return jsonify({'status': 'error', 'output': 'Invalid request format'}), 400

        script_name = data.get('script_name', 'ProXeek_main.py')
        params = data.get('params', {})

        print(f"Script name: {script_name}")
        if 'environmentImageBase64List' in params:
            print(f"Received {len(params['environmentImageBase64List'])} environment images")
        if 'virtualObjectSnapshots' in params:
            print(f"Received {len(params['virtualObjectSnapshots'])} virtual object snapshots")
        if 'arrangementSnapshots' in params:
            print(f"Received {len(params['arrangementSnapshots'])} arrangement snapshots")

        # Full path to the script
        script_path = os.path.join(SCRIPTS_PATH, script_name)

        if not os.path.exists(script_path):
            print(f"Script not found: {script_path}")
            return jsonify({'status': 'error', 'output': f'Script not found: {script_name}'}), 404

        # Create a temporary JSON file with parameters
        params_path = os.path.join(SCRIPTS_PATH, 'temp_params.json')
        with open(params_path, 'w') as f:
            json.dump(params, f)

        print(f"Running script: {sys.executable} {script_path} {params_path}")

        # Create a list to collect output
        output_lines = []

        def stream_output(pipe, prefix):
            """Read from pipe and print + store output"""
            for line in iter(pipe.readline, ''):
                if line:
                    formatted_line = f"[{prefix}] {line.rstrip()}"
                    print(formatted_line, flush=True)  # Print to Flask console
                    output_lines.append(line)
            pipe.close()

        # Run the Python script with real-time output streaming
        process = subprocess.Popen(
            [sys.executable, script_path, params_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,  # Line buffered
            universal_newlines=True
        )

        # Create threads to read stdout and stderr
        stdout_thread = threading.Thread(
            target=stream_output, 
            args=(process.stdout, "ProXeek")
        )
        stderr_thread = threading.Thread(
            target=stream_output, 
            args=(process.stderr, "ProXeek-ERROR")
        )

        stdout_thread.start()
        stderr_thread.start()

        # Wait for the process to complete
        return_code = process.wait(timeout=1000)
        
        # Wait for output threads to finish
        stdout_thread.join()
        stderr_thread.join()

        # Clean up the temporary file
        if os.path.exists(params_path):
            os.remove(params_path)

        # Join all output lines
        full_output = ''.join(output_lines)

        # Check for errors
        if return_code != 0:
            print(f"Script execution failed with code {return_code}")
            return jsonify({
                'status': 'error',
                'output': f"Error executing script (code {return_code}):\n{full_output}"
            }), 500

        # Return the output
        print(f"Script executed successfully, output length: {len(full_output)}")
        return jsonify({
            'status': 'success',
            'output': full_output
        })

    except Exception as e:
        print(f"Server error: {str(e)}")
        traceback.print_exc()
        return jsonify({'status': 'error', 'output': f"Server error: {str(e)}"}), 500


if __name__ == '__main__':
    print(f"Starting Python server on port 5000")
    print(f"Scripts path: {SCRIPTS_PATH}")
    app.run(host='0.0.0.0', port=5000, debug=True)