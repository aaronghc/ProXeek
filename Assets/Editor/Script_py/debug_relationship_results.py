#!/usr/bin/env python3

import json
import os

def analyze_relationship_results():
    """Analyze the current relationship rating results to identify issues"""
    
    script_dir = os.path.dirname(os.path.abspath(__file__))
    results_path = os.path.join(script_dir, "output", "relationship_rating_results.json")
    
    if not os.path.exists(results_path):
        print(f"Results file not found: {results_path}")
        return
    
    with open(results_path, 'r') as f:
        results = json.load(f)
    
    print(f"Total relationship rating results: {len(results)}")
    
    # Analyze dimension completeness
    dimension_stats = {"harmony": 0, "expressivity": 0, "realism": 0}
    missing_stats = {"harmony": 0, "expressivity": 0, "realism": 0}
    
    for i, result in enumerate(results):
        for dim in ["harmony", "expressivity", "realism"]:
            rating_key = f"{dim}_rating"
            explanation_key = f"{dim}_explanation"
            
            if rating_key in result and result[rating_key] not in [None, 0]:
                dimension_stats[dim] += 1
            else:
                missing_stats[dim] += 1
                
            # Check for mismatched explanations
            if explanation_key in result:
                explanation = result[explanation_key]
                contact_obj = result.get("physicalContactObject", "")
                substrate_obj = result.get("physicalSubstrateObject", "")
                
                # Look for object names in explanation that don't match the actual objects
                if "airpods" in explanation.lower() and "airpods" not in substrate_obj.lower():
                    print(f"Result {i}: {dim} explanation mentions 'airpods' but substrate is '{substrate_obj}'")
                if "gaming mouse" in explanation.lower() and "mouse" not in substrate_obj.lower():
                    print(f"Result {i}: {dim} explanation mentions 'gaming mouse' but substrate is '{substrate_obj}'")
    
    print("\nDimension Statistics:")
    for dim in ["harmony", "expressivity", "realism"]:
        total = dimension_stats[dim] + missing_stats[dim]
        print(f"  {dim}: {dimension_stats[dim]}/{total} present ({missing_stats[dim]} missing)")
    
    # Analyze unique pairs
    unique_pairs = set()
    for result in results:
        contact = result.get("physicalContactObject", "unknown")
        substrate = result.get("physicalSubstrateObject", "unknown")
        pair = f"{contact} -> {substrate}"
        unique_pairs.add(pair)
    
    print(f"\nUnique contact-substrate pairs: {len(unique_pairs)}")
    
    # Show first few pairs as examples
    print("\nExample pairs:")
    for i, pair in enumerate(sorted(unique_pairs)):
        if i < 5:
            print(f"  {pair}")
    
    # Look for explanation mismatches
    print("\nChecking for explanation mismatches...")
    mismatch_count = 0
    for i, result in enumerate(results):
        substrate_obj = result.get("physicalSubstrateObject", "").lower()
        
        for dim in ["harmony", "expressivity", "realism"]:
            explanation_key = f"{dim}_explanation"
            if explanation_key in result:
                explanation = result[explanation_key].lower()
                
                # Check if explanation mentions objects not in the substrate name
                mentioned_objects = []
                test_objects = ["airpods", "gaming mouse", "bottle", "headset", "laptop"]
                
                for obj in test_objects:
                    if obj in explanation and obj not in substrate_obj:
                        mentioned_objects.append(obj)
                
                if mentioned_objects:
                    print(f"  Result {i} ({dim}): mentions {mentioned_objects} but substrate is '{result.get('physicalSubstrateObject', '')}'")
                    mismatch_count += 1
    
    print(f"\nTotal explanation mismatches found: {mismatch_count}")

if __name__ == "__main__":
    analyze_relationship_results() 